﻿using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using SystemMonitor.Common;
using SystemMonitor.Common.Sdk;

namespace SystemMonitor.Monitors
{
    public sealed class DlnaMonitorAsync : IMonitorAsync
    {
        public string ServiceName => "DLNA/UPnP";
        public string ServiceDescription => "Monitors DLNA/UPnP server response.";
        public int Iteration { get; private set; }

        public string DisplayName => ServiceName;
        public int MonitorId { get;set; }
        public long TimeoutMilliseconds { get; set; }
        public string Host { get; set; } = string.Empty;
        public GraphType GraphType => GraphType.ResponseTime;

        /// <summary>
        /// True to force resolving of the Url instead of using the IP of the server
        /// </summary>
        public bool ResolveUrl { get; set; }
        public string Method { get; set; } = "GET";
        public Dictionary<string, string> Headers { get; set; } = new();
        public string? Body { get; set; }
        public string UserAgent { get; set; } = "6.2.9200 2/, UPnP/1.0, Portable SDK for UPnP devices/1.6.19";

        public string ConfigurationDescription => $"DLNA/UPnP monitor";
        private readonly ILogger _logger;

        public DlnaMonitorAsync(ILogger logger)
        {
            _logger = logger;
        }

        public void Dispose()
        {

        }

        public Task<IHostResponse> CheckHostAsync(IHost host, IConfigurationParameters parameters, CancellationToken cancelToken)
        {
            Iteration++;
            if (TimeoutMilliseconds <= 0)
                TimeoutMilliseconds = 5000;
            var response = HostResponse.Create();
            var startTime = DateTime.UtcNow;
            var port = 32469;
            try
            {
                if (parameters.Any())
                {
                    port = parameters.Get<int>("port", 32469);
                    ResolveUrl = parameters.Get<bool>("ResolveUrl");
                    if (parameters.Contains("Method"))
                        Method = parameters.Get<string>("Method");
                    if (parameters.Contains("UserAgent"))
                        UserAgent = parameters.Get<string>("UserAgent");
                    var headers = parameters.Get("Headers");
                    if (headers != null)
                    {
                        foreach (var token in headers)
                        {
                            if (!Headers.ContainsKey(token.Name))
                            {
                                if (token.Name == "UserAgent")
                                    UserAgent = token.Value.Value.ToString();
                                else
                                    Headers.Add(token.Name, token.Value.Value.ToString());
                            }
                        }
                    }
                    if (parameters.Contains("Body"))
                        Body = parameters.Get<string?>("Body");
                }

                var ipAddress = host.Ip ?? Util.HostToIp(host);

                if (!Equals(ipAddress, IPAddress.None))
                {
                    var isSuccessful = false;
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    var result = socket.BeginConnect(ipAddress, port > 0 ? port : 80, null, null);
                    var complete = result.AsyncWaitHandle.WaitOne((int)TimeoutMilliseconds, true);
                    if (complete && socket.Connected)
                    {
                        // connected! ask for a page
                        var header = $"{Method} /DeviceDescription.xml HTTP/1.1\r\n";
                        header += $"Host: {host.Hostname?.OriginalString ?? ipAddress.ToString()}:{port}\r\n";
                        //header += $"Content-Type: text/xml; charset=\"utf-8\"\r\n";
                        //header += $"Content-Length: {Body?.Length ?? 0}\r\n";
                        //header += "Date: Sat, 17 Jun 2023 02:25:36 GMT";
                        if (!string.IsNullOrEmpty(UserAgent))
                            header += $"User-Agent: {UserAgent}\r\n";
                        // write additional headers
                        foreach (var hdr in Headers)
                            header += $"{hdr.Key}: {hdr.Value}\r\n";
                        header += "Connection: close\r\n";
                        header += "\r\n";

                        if (!string.IsNullOrEmpty(Body))
                            header += Body;
                        header += "\r\n";

                        var buffer = Encoding.Default.GetBytes(header);
                        var bytesSent = socket.Send(buffer);
                        var so = new SocketObject();
                        so.WorkSocket = socket;
                        socket.BeginReceive(so.Buffer, 0, so.Buffer.Length, 0, ReceiveCallback, so);
                        complete = so.AllDone.WaitOne((int)TimeoutMilliseconds);
                        response.ResponseTime = DateTime.UtcNow - startTime;
                        if (complete)
                        {
                            // look for any non 400-500 response
                            // UPnP/1.0 DLNADOC/1.50 Platinum/1.0.5.13
                            var responseMessage = so.Sb.ToString();
                            if (responseMessage.Length > 0)
                            {
                                // using an optimized routine to grab http status code instead of using string split(s)
                                var eol = responseMessage.IndexOf("\r\n", 8, StringComparison.Ordinal);
                                var line1 = responseMessage.Substring(0, eol);
                                var soc = line1.IndexOf(" ", StringComparison.Ordinal);
                                if (soc >= 0 && line1.Length > soc)
                                {
                                    soc += 1;
                                    var responseCodeStr = line1.Substring(soc, 3);
                                    if (int.TryParse(responseCodeStr, out var responseCode))
                                    {
                                        response.Value = responseCode;
                                        isSuccessful = responseCode == 200 && (responseMessage.Contains("UPnP/", StringComparison.InvariantCultureIgnoreCase) || responseMessage.Contains("DLNADOC/", StringComparison.InvariantCultureIgnoreCase));

                                        // get the xml info
                                        var indexOfBody = responseMessage.IndexOf("\r\n\r\n");
                                        var xmlStr = responseMessage.Substring(indexOfBody + 4);
                                        var xmlDoc = new XmlDocument();
                                        xmlDoc.LoadXml(xmlStr);
                                        var friendlyName = xmlDoc.DocumentElement.SelectSingleNode("/*[local-name()='root']/*[local-name()='device']/*[local-name()='friendlyName']")?.InnerText;
                                        var modelName = xmlDoc.SelectSingleNode("/*[local-name()='root']/*[local-name()='device']/*[local-name()='modelName']")?.InnerText;
                                        //var modelDescription = xmlDoc.SelectSingleNode("/*[local-name()='root']/*[local-name()='device']/*[local-name()='modelDescription']")?.InnerText;
                                        response.State = modelName ?? friendlyName;
                                    }
                                }
                            }
                        }

                        so.Dispose();
                    }
                    response.IsUp = isSuccessful;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception thrown in {nameof(DlnaMonitorAsync)}");
                response.IsUp = false;
            }
            return Task.FromResult(response);
        }

        private void ReceiveCallback(IAsyncResult result)
        {
            var so = (SocketObject?)result.AsyncState;
            if (so == null)
                return;
            var s = so.WorkSocket;
            if (s == null)
                return;
            try
            {
                var read = s.EndReceive(result);
                if (read > 0)
                {
                    so.Sb.Append(Encoding.Default.GetString(so.Buffer, 0, read));
                    // we only care about the header
                    /*if (so.Sb.ToString().Contains("\r\n\r\n"))
                    {
                        s.Close();
                        so.AllDone.Set();
                    }
                    else
                    {*/
                        s.BeginReceive(so.Buffer, 0, so.Buffer.Length, 0, ReceiveCallback, so);
                    //}
                }
                else
                {
                    s.Close();
                    so.AllDone.Set();
                }
            }
            catch (Exception)
            {
                s?.Close();
                so.AllDone.Set();
            }
        }

        private class SocketObject : IDisposable
        {
            private const int BufferSize = 8192;
            public readonly ManualResetEvent AllDone = new(false);
            public Socket? WorkSocket;
            public readonly byte[] Buffer = new byte[BufferSize];
            public readonly StringBuilder Sb = new();

            public void Dispose()
            {
                AllDone.Close();
                WorkSocket?.Close();
                WorkSocket?.Dispose();
                Sb.Clear();
            }
        }
    }
}
