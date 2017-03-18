using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using ImageResizer;
using Intellipix.Models;
using Microsoft.Azure;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Threading.Tasks;
using System.IO;
using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Vision;


namespace Intellipix.Controllers
{
    public class HomeController : Controller
    {
        [HttpPost]
        public ActionResult Search(string term)
        {
            return RedirectToAction("Index", new { id = term });
        }

        public ActionResult Index(string id)
        {
            // Pass a list of blob URIs and captions in ViewBag
            CloudStorageAccount account = CloudStorageAccount
                .Parse(CloudConfigurationManager
                .GetSetting("StorageConnectionString"));
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference("photos");
            List<BlobInfo> blobs = new List<BlobInfo>();

            foreach (IListBlobItem item in container.ListBlobs())
            {
                var blob = item as CloudBlockBlob;

                if (blob != null)
                {
                    blob.FetchAttributes(); // Get blob metadata

                    if (String.IsNullOrEmpty(id) || HasMatchingMetadata(blob, id))
                    {
                        // "caption" is a key associated with the photo's metadata
                        var caption = blob.Metadata.ContainsKey("Caption") ? blob.Metadata["Caption"] : blob.Name;
                        var age = blob.Metadata.ContainsKey("Age") ? blob.Metadata["Age"] : "Unknown";
                        var gender = blob.Metadata.ContainsKey("Gender") ? blob.Metadata["Gender"] : "Unknown";
                        var keysArray = blob.Metadata.Keys;
                        var tags = new List<string>();
                        foreach(var key in keysArray)
                        {
                            if (key.StartsWith("Tag"))
                                tags.Add(blob.Metadata[key]);
                        }
                        var tagsAsString = String.Join(" ", tags.ToArray());

                        blobs.Add(new BlobInfo()
                        {
                            ImageUri = blob.Uri.ToString(),
                            ThumbnailUri = blob.Uri.ToString().Replace("/photos/", "/thumbnails/"),
                            Caption = caption,
                            Tags = tagsAsString,
                            Gender = gender,
                            Age = age
                        });
                    }
                }
            }

            ViewBag.Blobs = blobs.ToArray();
            ViewBag.Search = id; // Prevent search box from losing its content
            return View();
        }

        private bool HasMatchingMetadata(CloudBlockBlob blob, string term)
        {
            foreach (var item in blob.Metadata)
            {
                if (item.Key.StartsWith("Tag") && 
                    item.Value.Equals(term, StringComparison.InvariantCultureIgnoreCase))
                    return true;
            }

            return false;
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }

        [HttpPost]
        public async Task<ActionResult> Upload(HttpPostedFileBase file)
        {
            if (file != null && file.ContentLength > 0)
            {
                // Make sure the user selected an image file
                if (!file.ContentType.StartsWith("image"))
                {
                    TempData["Message"] = "Only image files may be uploaded";
                }
                else
                {
                    // Save the original image in the "photos" container
                    CloudStorageAccount account = CloudStorageAccount
                        .Parse(CloudConfigurationManager
                        .GetSetting("StorageConnectionString"));
                    CloudBlobClient client = account.CreateCloudBlobClient();
                    CloudBlobContainer container = client.GetContainerReference("photos");
                    CloudBlockBlob photo = container
                        .GetBlockBlobReference(Path.GetFileName(file.FileName));
                    await photo.UploadFromStreamAsync(file.InputStream);
                    file.InputStream.Seek(0L, SeekOrigin.Begin);

                    // Generate a thumbnail and save it in the "thumbnails" container
                    using (var outputStream = new MemoryStream())
                    {
                        var settings = new ResizeSettings { MaxWidth = 192, Format = "png" };
                        ImageBuilder.Current.Build(file.InputStream, outputStream, settings);
                        outputStream.Seek(0L, SeekOrigin.Begin);
                        container = client.GetContainerReference("thumbnails");
                        CloudBlockBlob thumbnail = container
                            .GetBlockBlobReference(Path.GetFileName(file.FileName));
                        await thumbnail.UploadFromStreamAsync(outputStream);
                    }

                    //..............................................................................
                    // Submit the image to Azure's Computer Vision API
                    //TODO: Make only one call to cog services to improve speed
                    // Currently there are two calls: 
                    // one for tags and captions, another call for age and gender

                    // Call 1 for tags and caption
                    VisionServiceClient visionClient = new VisionServiceClient(CloudConfigurationManager
                        .GetSetting("SubscriptionKey")); // Maks's key
                    VisualFeature[] visualFeatures = new VisualFeature[] 
                    {
                        VisualFeature.Description
                    };
                    var resultVision = await visionClient.AnalyzeImageAsync(photo.Uri.ToString(), visualFeatures);

                    // Call 2 for gender and age 
                    VisionServiceClient faceClient = new VisionServiceClient(CloudConfigurationManager
                        .GetSetting("SubscriptionKey"));
                    VisualFeature[] faceFeatures = new VisualFeature[]
                    {
                        VisualFeature.Faces
                    };
                    var resultFaces = await faceClient.AnalyzeImageAsync(photo.Uri.ToString(), faceFeatures);


                    // Record the image description and tags in blob metadata
                    photo.Metadata.Add("Caption", resultVision.Description.Captions[0].Text);
                    //TODO: needs a null guard for Gender and Age 
                    photo.Metadata.Add("Gender", resultFaces.Faces[0].Gender);
                    photo.Metadata.Add("Age", resultFaces.Faces[0].Age.ToString());

                    for (int i = 0; i < resultVision.Description.Tags.Length; i++)
                    {
                        string key = String.Format("Tag{0}", i);
                        photo.Metadata.Add(key, resultVision.Description.Tags[i]);
                    }

                    await photo.SetMetadataAsync();
                }
            }

            // redirect back to the index action to show the form once again
            return RedirectToAction("Index");
        }
    }
}