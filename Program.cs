using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;

namespace PageBlobUnmanagedDiskResize
{
    internal static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            string connectionString, blobContainerName, blobName;
            try
            {
                connectionString = GetRequiredInput("AZURE_STORAGE_CONNECTION_STRING");
                blobContainerName = GetRequiredInput("AZURE_STORAGE_CONTAINER_NAME");
                blobName = GetRequiredInput("AZURE_STORAGE_BLOB_NAME");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                return -1;
            }

            // See: https://docs.microsoft.com/en-us/azure/storage/blobs/storage-blob-pageblob-overview
            var client = new BlobServiceClient(connectionString);
            BlobContainerClient containerClient = client.GetBlobContainerClient(blobContainerName);
            PageBlobClient pageBlobClient = containerClient.GetPageBlobClient(blobName);

            IEnumerable<HttpRange> pageRanges = (await pageBlobClient.GetPageRangesAsync()).Value.PageRanges;
            foreach (HttpRange range in pageRanges)
            {
                await Console.Out.WriteLineAsync(range.ToString());
            }
            
            // Next steps:  
            // https://technet2.github.io/Wiki/blogs/windowsazurestorage/using-windows-azure-page-blobs-and-how-to-efficiently-upload-and-download-page-blobs.html

            return 0;
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
