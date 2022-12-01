using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace SmokeyVersionSwitcher
{
    class BadUpdateIdentityException : ArgumentException
    {
        public BadUpdateIdentityException() : base("Bad updateIdentity") { }
    }

    class Downloader
    {
        private readonly HttpClient client = new HttpClient();
        private readonly WUProtocol protocol = new WUProtocol();

        private async Task<XDocument> PostXmlAsync(string url, XDocument data)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);

            using (StringWriter stringWriter = new StringWriter())
            {
                using (XmlWriter xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings { Indent = false, OmitXmlDeclaration = true }))
                {
                    data.Save(xmlWriter);
                }

                request.Content = new StringContent(stringWriter.ToString(), Encoding.UTF8, "application/soap+xml");
            }

            using (HttpResponseMessage response = await client.SendAsync(request))
            {
                string str = await response.Content.ReadAsStringAsync();
                return XDocument.Parse(str);
            }
        }

        private async Task DownloadFile(string url, string to, DownloadProgress progress, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                using (Stream inStream = await response.Content.ReadAsStreamAsync())

                using (FileStream outStream = new FileStream(to, FileMode.Create))
                {
                    long? totalSize = response.Content.Headers.ContentLength;
                    progress(0, totalSize);
                    long transferred = 0;
                    byte[] buf = new byte[1024 * 1024];

                    while (true)
                    {
                        int n = await inStream.ReadAsync(buf, 0, buf.Length, cancellationToken);
                        if (n == 0)
                        {
                            break;
                        }

                        await outStream.WriteAsync(buf, 0, n, cancellationToken);
                        transferred += n;
                        progress(transferred, totalSize);
                    }
                }
            }
        }

        private async Task<string> GetDownloadUrl(string updateIdentity, string revisionNumber)
        {
            XDocument result = await PostXmlAsync(protocol.GetDownloadUrl(),
                protocol.BuildDownloadRequest(updateIdentity, revisionNumber));

            foreach (string resultUrl in protocol.ExtractDownloadResponseUrls(result))
            {
                if (resultUrl.StartsWith("http://tlu.dl.delivery.mp.microsoft.com/"))
                {
                    return resultUrl;
                }
            }

            return null;
        }

        public void EnableUserAuthorization()
        {
            protocol.SetMSAUserToken(WUTokenHelper.GetWUToken());
        }

        public async Task Download(string updateIdentity, string revisionNumber, string destination, DownloadProgress progress, CancellationToken cancellationToken)
        {
            string link = await GetDownloadUrl(updateIdentity, revisionNumber);

            if (link == null)
            {
                throw new BadUpdateIdentityException();
            }

            Debug.WriteLine("Resolved download link: " + link);
            await DownloadFile(link, destination, progress, cancellationToken);
        }

        public delegate void DownloadProgress(long current, long? total);
    }
}
