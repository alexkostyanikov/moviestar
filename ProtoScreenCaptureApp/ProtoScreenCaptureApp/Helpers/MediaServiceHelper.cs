using System.Configuration;
using System.Threading;
using Microsoft.WindowsAzure.MediaServices.Client;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ProtoScreenCaptureApp.Helpers
{
    public class MediaServiceHelper
    {
        private static CloudMediaContext _context = null;
        private static readonly string AccountKey = ConfigurationManager.AppSettings["accountKey"];
        private static readonly string AccountName = ConfigurationManager.AppSettings["accountName"];
        private const string SmoothStreamingPreset = "VC1 Smooth Streaming 720p";
        private const string Mpeg4Preset = "H264 Broadband 720p"; // see more: http://msdn.microsoft.com/en-us/library/windowsazure/jj129582.aspx
         
        static private IAsset CreateEmptyAsset(string assetName, AssetCreationOptions assetCreationOptions)
        {
            var asset = default(IAsset);
            if (_context == null)
                _context = new CloudMediaContext(AccountName, AccountKey);
            asset = _context.Assets.Create(assetName, assetCreationOptions);
            return asset;
        }

        static public IAsset CreateAssetAndUploadSingleFile(AssetCreationOptions assetCreationOptions, string singleFilePath, Action<string> updateStatus)
        {
            var assetName = "UploadSingleFile_" + DateTime.UtcNow.ToString();
            var asset = CreateEmptyAsset(assetName, assetCreationOptions);
            var fileName = Path.GetFileName(singleFilePath);
            var assetFile = asset.AssetFiles.Create(fileName);
            updateStatus("asset created");

            var accessPolicy = _context.AccessPolicies.Create(assetName, TimeSpan.FromDays(3), AccessPermissions.Write | AccessPermissions.List);
            var locator = _context.Locators.CreateLocator(LocatorType.Sas, asset, accessPolicy);
            updateStatus("uploading");

            assetFile.Upload(singleFilePath);
            updateStatus("uploaded");

            locator.Delete();
            accessPolicy.Delete();
            return asset;
        }

        static public IJob CreateEncodingJob(IAsset asset, Action<string> updateStatus, Action<string> updateResultUrl )
        {
            var job = _context.Jobs.Create("Screen capture encoding job");
            var processor = GetLatestMediaProcessorByName("Windows Azure Media Encoder");
            var task = job.Tasks.AddNew("My encoding task", processor, Mpeg4Preset, TaskOptions.ProtectedConfiguration);
            task.InputAssets.Add(asset);
            task.OutputAssets.AddNew("Output asset", AssetCreationOptions.None);
            job.StateChanged += (sender, e) => CurrentConvertStateChanged(e, updateStatus);
            job.Submit();

            var progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);
            progressJobTask.Wait();

            job = GetJob(job.Id);
            if (job.State == JobState.Error)
                return job;

            var outputAsset = job.OutputMediaAssets[0];
            IAccessPolicy policy = null;
            ILocator locator = null;

            // Declare an access policy for permissions on the asset. 
            // You can call an async or sync create method. 
            policy = _context.AccessPolicies.Create("My 30 days readonly policy", TimeSpan.FromDays(30), AccessPermissions.Read);

            // Create a SAS locator to enable direct access to the asset 
            // in blob storage. You can call a sync or async create method.  
            // You can set the optional startTime param as 5 minutes 
            // earlier than Now to compensate for differences in time  
            // between the client and server clocks. 

            locator = _context.Locators.CreateLocator(LocatorType.Sas, outputAsset, policy, DateTime.UtcNow.AddMinutes(-5));

            // Build a list of SAS URLs to each file in the asset. 
            var sasUrlList = GetAssetSasUrlList(outputAsset, locator);

            // Write the URL list to a local file. You can use the saved 
            // SAS URLs to browse directly to the files in the asset.
            if (sasUrlList != null)
                updateResultUrl(sasUrlList.FirstOrDefault());
            return job;
        }

       private static void CurrentConvertStateChanged(JobStateChangedEventArgs e, Action<string> updateStatus ) 
        {
            switch (e.CurrentState)
            {
                case JobState.Finished:
                    updateStatus("finished");
                    break;
                case JobState.Canceling:
                    updateStatus("canceling");
                    break;
                case JobState.Queued:
                    updateStatus("queued");
                    break;
                case JobState.Scheduled:
                    updateStatus("scheduled");
                    break;
                case JobState.Processing:
                    updateStatus("processing");
                    break;
                case JobState.Canceled:
                    updateStatus("canceled");
                    break;
                case JobState.Error:
                    updateStatus("error");
                    break;
            }
        }

        private static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
        {
            // The possible strings that can be passed into the 
            // method for the mediaProcessor parameter:
            //   Windows Azure Media Encoder
            //   Windows Azure Media Packager
            //   Windows Azure Media Encryptor
            //   Storage Decryption

            var processor = _context.MediaProcessors.Where(p => p.Name == mediaProcessorName).
                ToList().OrderBy(p => new Version(p.Version)).LastOrDefault();

            if (processor == null)
                throw new ArgumentException(string.Format("Unknown media processor", mediaProcessorName));

            return processor;
        }

        static IJob GetJob(string jobId)
        {
            return _context.Jobs.ToList().FirstOrDefault(i => i.Id == jobId);
        }

        static IEnumerable<string> GetAssetSasUrlList(IAsset asset, ILocator locator)
        {
            return asset.AssetFiles.ToList().Where(i=> i.MimeType =="video/mp4").Select(file => BuildFileSasUrl(file, locator));
        }

        // Create and return a SAS URL to a single file in an asset. 
        static string BuildFileSasUrl(IAssetFile file, ILocator locator)
        {
            // Take the locator path, add the file name, and build 
            // a full SAS URL to access this file. This is the only 
            // code required to build the full URL.
            
            var uriBuilder = new UriBuilder(locator.Path);
            uriBuilder.Path += "/" + file.Name;
            return uriBuilder.Uri.AbsoluteUri;
        }
    }
}
