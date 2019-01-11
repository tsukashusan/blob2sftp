using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Renci.SshNet.Async;
using Renci.SshNet;

namespace blob2sftp
{
    public static class Function1
    {
        static string accountName = "<AccountName>";
        static string accessKey = "<AccessKey>";
        static StorageCredentials credential = new StorageCredentials(accountName, accessKey);
        static CloudStorageAccount storageAccount = new CloudStorageAccount(credential, true);

        [FunctionName("Function1")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");
            string requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            dynamic datas = JsonConvert.DeserializeObject(requestBody);
            foreach (var eventData in datas)
            {
                if (((string)eventData.data.eventType).Trim() == "Microsoft.EventGrid.SubscriptionValidationEvent")
                {
                    var validationCode = eventData.data.validationCode;
                    string s = validationCode.ToString();
                    var r = $"{{\"validationResponse\":\"{validationCode}\"}}";
                    return validationCode != null
                        ? (ActionResult)new OkObjectResult(r)
                        : new BadRequestObjectResult("Please pass a name on the query string or in the request body");
                }
                else
                { 
                    try
                    {
                        var url = new Uri(eventData.data.url.ToString());
                        var container = url.AbsolutePath.Split('/')[1];
                        int startIndex = "/".Length + container.Length + "/".Length;
                        int length = url.AbsolutePath.Length - startIndex;
                        var blobname = url.AbsolutePath.Substring(startIndex, length);
                        var blobNameArray = blobname.Split('/');
                        var blobFileName = blobNameArray[blobNameArray.Length - 1];

                        var tempPath = Path.GetTempPath();
                        var tempFilePath = Path.Combine(Path.GetTempPath(), blobFileName);

                        //blob
                        CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                        //container
                        CloudBlobContainer blobcontainer = blobClient.GetContainerReference(container);

                        //ダウンロードするファイル名を指定
                        CloudBlockBlob blockBlob_download = blobcontainer.GetBlockBlobReference(blobname);

                        //ダウンロード処理
                        //ダウンロード後のパスとファイル名を指定。
                        var downloadFile = $"{tempFilePath}";
                        await blockBlob_download.DownloadToFileAsync(downloadFile, System.IO.FileMode.OpenOrCreate);
                        log.LogInformation("blob download successful.");


                        await UploadSFTP($"{downloadFile}", blobFileName, log);
                        File.Delete(downloadFile);
                        log.LogInformation("sftp upload successful.");

                    }
                    catch (Exception e)
                    {
                        log.LogCritical(e.Message);
                    }
                }
            }
            return (ActionResult)new OkObjectResult("");

        }

        private static async Task UploadSFTP(string filePath, string fileName, ILogger log)
        {
            //setup client
            using (var client = new SftpClient("<sftphostname>", "<userid>", "<password>"))
            {
                client.Connect();
                // await a file upload
                using (var localStream = File.OpenRead(filePath))
                {
                    client.ChangeDirectory("<put directory>");
                    await client.UploadAsync(localStream, $"{fileName}");
                    // disconnect like you normally would
                    client.Disconnect();
                }
            }
        }
    }
}

