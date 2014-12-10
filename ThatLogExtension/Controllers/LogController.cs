﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.UI.WebControls.WebParts;
using ThatLogExtension.Models;

namespace ThatLogExtension.Controllers
{
    public class LogController : ApiController
    {
        private static readonly Dictionary<string, ILogBrowser> LogBrowsers = new Dictionary<string, ILogBrowser>(StringComparer.InvariantCultureIgnoreCase);

        static LogController()
        {
            AddFileSystemLogBrowserBasedOnExistance(LogBrowsers, "c:\\temp", "filesystemtemp", "File System - Temp");
            AddFileSystemLogBrowserBasedOnExistance(LogBrowsers, "LogFiles\\Application", "filesystemapplication", "File System - Application Logs");
            AddFileSystemLogBrowserBasedOnExistance(LogBrowsers, "LogFiles\\http\\RawLogs", "filesystemhttp", "File System - HTTP Logs");
            AddFileSystemLogBrowserBasedOnExistance(LogBrowsers, "LogFiles\\DetailedErrors", "detailederrors", "IIS Detailed Errors");
            AddFileSystemLogBrowserBasedOnExistance(LogBrowsers, "LogFiles\\kudu\\trace", "filesystemkudu", "File System - Kudu Logs");
            AddFileSystemLogBrowserBasedOnExistance(LogBrowsers, "LogFiles", "filesystem", "File System - Log Files Directory");

            AddStorageLogBrowserBasedOnEnvironment(LogBrowsers, "DIAGNOSTICS_AZUREBLOBCONTAINERSASURL", "blobapplication", "Application Logs - Blob Storage");
            AddStorageLogBrowserBasedOnEnvironment(LogBrowsers, "DIAGNOSTICS_AZURETABLESASURL", "tableapplication", "Application Logs - Table Storage", tableStorage: true);
            AddStorageLogBrowserBasedOnEnvironment(LogBrowsers, "WEBSITE_HTTPLOGGING_CONTAINER_URL", "blobhttp", "HTTP Logs - Blob Storage");
        }

        private static void AddStorageLogBrowserBasedOnEnvironment(Dictionary<string, ILogBrowser> logBrowsers, string environmentVariableKey, string logBrowserKey, string logBrowserName, bool tableStorage = false)
        {
            var sasUrl = ConfigurationManager.AppSettings[environmentVariableKey];
            if (sasUrl != null)
            {
                if (!tableStorage)
                {
                    logBrowsers.Add(logBrowserKey, new StorageLogBrowser(logBrowserName, sasUrl));
                }
                else
                {
                    logBrowsers.Add(logBrowserKey, new TableStorageLogBrowser(logBrowserName));
                }
            }
        }

        private static void AddFileSystemLogBrowserBasedOnExistance(Dictionary<string, ILogBrowser> logBrowsers, string fileSystemPath, string logBrowserKey, string logBrowserName)
        {
            fileSystemPath = Path.Combine(Environment.ExpandEnvironmentVariables("%HOME%"), fileSystemPath);

            if (Directory.Exists(fileSystemPath))
            {
                logBrowsers.Add(logBrowserKey, new FileSystemLogBrowser(logBrowserName, fileSystemPath));
            }
        }

        public HttpResponseMessage Get(string path)
        {
            string itemPath;
            string type = ExtractTypeFromPath(path, out itemPath);

            string urlPath = Request.RequestUri.AbsolutePath.TrimEnd('/');
            int hashIndex = urlPath.IndexOf('#');
            if (hashIndex > 0)
            {
                urlPath = urlPath.Remove(hashIndex);
            }

            if (String.IsNullOrEmpty(type))
            {
                return Request.CreateResponse(
                    HttpStatusCode.OK,
                    new LogItems()
                    {
                        IsDirectory = true,
                        Items = LogBrowsers.Select(keyValuePair => new LogItem()
                        {
                            Name = keyValuePair.Value.Name,
                            IsDirectory = true,
                            Url = urlPath + "?path=/" + keyValuePair.Key,
                            ExternalUrl = keyValuePair.Value.BuildExternalUrl()
                        })
                    });
            }

            ILogBrowser logBrowser;
            if (!LogBrowsers.TryGetValue(type, out logBrowser))
            {
                return Request.CreateResponse(HttpStatusCode.NotFound);
            }

            return Request.CreateResponse(HttpStatusCode.OK, logBrowser.GetLogItem(urlPath + "?path=" + path, itemPath));
        }

        [HttpGet]
        public async Task<HttpResponseMessage> Download(string path, bool download)
        {
            string itemPath;
            string type = ExtractTypeFromPath(path, out itemPath);

            ILogBrowser logBrowser;
            if (!LogBrowsers.TryGetValue(type, out logBrowser))
            {
                return Request.CreateResponse(HttpStatusCode.NotFound);
            }

            Stream stream = await logBrowser.GetStreamForDownloadAsync(itemPath);

            var result = new HttpResponseMessage(HttpStatusCode.OK);
            result.Content = new StreamContent(stream);
            result.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            result.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
            {
                FileName = Path.GetFileName(itemPath)
            };

            return result;
        }

        private static string ExtractTypeFromPath(string path, out string itemPath)
        {
            string type = null;
            itemPath = null;
            if (path != null)
            {
                var parts = path.Split(new char[] { '/' }, 2, StringSplitOptions.RemoveEmptyEntries);
                type = parts.Length > 0 ? parts[0] : null;
                itemPath = parts.Length > 1 ? parts[1] : null;
            }
            return type;
        }
    }
}