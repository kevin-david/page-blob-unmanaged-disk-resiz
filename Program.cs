using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Resources;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Azure;
using Microsoft.VisualBasic.CompilerServices;

namespace PageBlobUnmanagedDiskResize
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            try
            {
                await DoWorkAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                return -1;
            }

            return 0;
        }

        private static async Task DoWorkAsync()
        {
            string connectionString = GetRequiredInput("AZURE_STORAGE_CONNECTION_STRING");
            string blobContainerName = GetRequiredInput("AZURE_STORAGE_CONTAINER_NAME");
            string blobNames = GetRequiredInput("AZURE_STORAGE_BLOB_NAME");

            // See: https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-pageblob-overview
            var client = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = client.GetBlobContainerClient(blobContainerName);
            foreach (var blobName in blobNames.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                Console.WriteLine($"Blob: {blobName}");
                PageBlobClient pageBlobClient = containerClient.GetPageBlobClient(blobName);

                Console.WriteLine("Requesting pages...");
                List<HttpRange> pageRanges = (await pageBlobClient.GetPageRangesAsync()).Value.PageRanges.ToList();
                long sizeBytes = 0;
                foreach (HttpRange range in pageRanges)
                {
                    Console.WriteLine($"\t{range.ToString()}");
                    sizeBytes += range.Length ?? 0;
                }

                HttpRange trailerRange = pageRanges[^1];
                Console.WriteLine($"Downloading trailer page of {trailerRange.Length} bytes...");
                Response<BlobDownloadInfo> lastPageDownload = await pageBlobClient.DownloadAsync(trailerRange);

                var lastPage = new MemoryStream(capacity: (int)trailerRange.Length!.Value);
                await lastPageDownload.Value.Content.CopyToAsync(lastPage);
                lastPage.Seek(0, SeekOrigin.Begin);

                using (var sr = new StreamReader(lastPage, leaveOpen: true))
                {
                    Console.WriteLine($"Trailer range: {await sr.ReadToEndAsync()}");
                }

                lastPage.Seek(0, SeekOrigin.Begin);

                var lastDataRange = pageRanges[^2];
                long firstByteOfNextRange = (lastDataRange.Offset + lastDataRange.Length)!.Value;
                Console.WriteLine($"ASSUMPTION: Last data byte is at position {firstByteOfNextRange - 1}");
                Console.WriteLine("⚠️  IF THIS IS WRONG, STOP NOW!! ⚠️");
                Console.WriteLine();
                Console.WriteLine($"[Total size of all pages: {sizeBytes / 1024.0 / 1024.0 / 1024.0} GiB]");

                Console.WriteLine("\n=======================\n");

                long desiredSize = firstByteOfNextRange + lastPage.Length;
                Console.WriteLine($"Will resize to {desiredSize}, rewriting trailer to {firstByteOfNextRange}-{desiredSize} are you SURE?");
                Console.WriteLine("ℹ️  If you haven't yet, you should probably create a snapshot BEFORE you do this!");
                Console.WriteLine("This will CLEAR any pages after this offset!!!");
                Console.Write("[y/n]? ");

                var result = Console.ReadKey();
                if (result.KeyChar == 'y')
                {
                    Console.WriteLine();

                    Console.Write(
                        $"⛔️️ NO WARRANTY: THIS WILL CLEAR TRAILER PAGE(S), RESULTING IN POTENTIAL DATA LOSS. Are you REALLY sure? Did you take a backup/snapshot?? [y/n] ");
                    result = Console.ReadKey();
                    if (result.KeyChar == 'y')
                    {
                        Console.WriteLine();

                        int[] countdown = new[] { 5, 4, 3, 2, 1 };
                        Console.Write("Last chance, resizing in ");
                        foreach (int num in countdown)
                        {
                            Console.Write(num + ".. ");
                            await Task.Delay(1000);
                        }

                        Console.WriteLine();

                        Console.WriteLine($"Writing trailer to {firstByteOfNextRange}... ");
                        Response<PageInfo> writeTrailerResult =
                            await pageBlobClient.UploadPagesAsync(lastPage, firstByteOfNextRange);

                        Console.WriteLine(
                            $"Rewrote trailer successfully on {writeTrailerResult.Value.LastModified}. Proceeding with resize to {desiredSize} bytes... ");
                        
                        Response<PageBlobInfo> resizeResult = await pageBlobClient.ResizeAsync(desiredSize);
                        Console.WriteLine($"Resize successful on {resizeResult.Value.LastModified}..");
                        Console.WriteLine($"Done with blob: {blobName}");
                    }
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine($"{result.Key} was pressed, skipping...");
                }
            }

            // Next steps:  
            // https://technet2.github.io/Wiki/blogs/windowsazurestorage/using-windows-azure-page-blobs-and-how-to-efficiently-upload-and-download-page-blobs.html
        }


        private static string GetRequiredInput(string envVarName)
        {
            string? value = Environment.GetEnvironmentVariable(envVarName);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"Somewhat-valid value for Environment Variable `{envVarName}` required.");
            }

            return value;
        }
    }
}
