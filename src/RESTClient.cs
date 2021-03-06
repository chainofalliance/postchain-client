﻿using System;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

#if UNITYBUILD
using UnityEngine.Networking;
#else
using System.IO;
using System.Net;
#endif

namespace Chromia.Postchain.Client
{
    internal class HTTPStatusResponse
    {
        public HTTPStatusResponse(string status, string message)
        {
            this.status = status;
            this.message = message;
        }

        public string status = "";
        public string message = "";
    }

    public class RESTClient
    {
        public string BlockchainRID {get; private set;}
        public int RequestTimeout {
            get => _requestTimout;
            set
            {
                if (value >= 0 || value == System.Threading.Timeout.Infinite)
                {
                    _requestTimout = value;
                }
            }
        }

        private string _urlBase;
        private int _requestTimout = 1000;

        ///<summary>
        ///Create new RESTClient object.
        ///</summary>
        ///<param name = "urlBase">URL to rest server.</param>
        ///<param name = "blockchainRID">RID of blockchain.</param>
        public RESTClient(string urlBase, string blockchainRID = null)
        {
            BlockchainRID = blockchainRID;
            _urlBase = urlBase;
        }

        public async Task<PostchainErrorControl> InitializeBRIDFromChainID(int chainID)
        {
            var brid = await Get<string>(this._urlBase, "brid/iid_" + chainID, true);            
            if (brid is HTTPStatusResponse)
            {
                return new PostchainErrorControl(true, ((HTTPStatusResponse) brid).message);
            }
            else if (brid is string)
            {
                this.BlockchainRID = (string) brid;

                return new PostchainErrorControl();
            }
            else
            {
                return new PostchainErrorControl(true, "Unknown query return type " + brid.GetType().ToString());
            }
        }

        internal async Task<object> PostTransaction(string serializedTransaction)
        {
            string jsonString = String.Format(@"{{""tx"": ""{0}""}}", serializedTransaction);
            
            return await Post<HTTPStatusResponse>(this._urlBase, "tx/" + this.BlockchainRID, jsonString);
        }

        internal async Task<PostchainErrorControl> PostAndWaitConfirmation(string serializedTransaction, string txRID)
        {
            await this.PostTransaction(serializedTransaction);

            return await this.WaitConfirmation(txRID);
        }

        internal async Task<object> Query<T>(string queryName, (string name, object content)[] queryObject)
        {
            var queryDict = QueryToDict(queryName, queryObject);
            string queryString = JsonConvert.SerializeObject(queryDict);

            return await Post<T>(this._urlBase, "query/" + this.BlockchainRID, queryString);
        }

        private async Task<HTTPStatusResponse> Status(string messageHash)
        {
            ValidateMessageHash(messageHash);
            return (HTTPStatusResponse) await Get<HTTPStatusResponse>(this._urlBase, "tx/" + this.BlockchainRID + "/" + messageHash + "/status");
        }

        private Dictionary<string, object> QueryToDict(string queryName, (string name, object content)[] queryObject)
        {
            var queryDict = new Dictionary<string, object>();

            queryDict.Add("type", queryName);
            foreach (var entry in queryObject)
            {
                if (entry.content is byte[])
                {
                    queryDict.Add(entry.name, PostchainUtil.ByteArrayToString((byte[]) entry.content));
                }
                else
                {
                    queryDict.Add(entry.name, entry.content);
                }
            }

            return queryDict;
        }

        private static bool IsTuple(Type tuple)
        {
            if (!tuple.IsGenericType)
                return false;
            var openType = tuple.GetGenericTypeDefinition();
            return openType == typeof(ValueTuple<>)
                || openType == typeof(ValueTuple<,>)
                || openType == typeof(ValueTuple<,,>)
                || openType == typeof(ValueTuple<,,,>)
                || openType == typeof(ValueTuple<,,,,>)
                || openType == typeof(ValueTuple<,,,,,>)
                || openType == typeof(ValueTuple<,,,,,,>)
                || (openType == typeof(ValueTuple<,,,,,,,>) && IsTuple(tuple.GetGenericArguments()[7]));
        }

        private async Task<PostchainErrorControl> WaitConfirmation(string txRID)
        {
            var response = await this.Status(txRID);

            foreach(System.ComponentModel.PropertyDescriptor descriptor in System.ComponentModel.TypeDescriptor.GetProperties(response))
            {
                string name=descriptor.Name;
                object value=descriptor.GetValue(response);
                Console.WriteLine("{0}={1}",name,value);
            }

            switch(response.status)
            {
                case "confirmed":
                    return new PostchainErrorControl(false, "");
                case "rejected":
                case "unknown":
                    return new PostchainErrorControl(true, "Message was rejected");
                case "waiting":
                    await Task.Delay(511);
                    return await this.WaitConfirmation(txRID);
                case "exception":
                    return new PostchainErrorControl(true, "HTTP Exception: " + response.message);
                default:
                    return new PostchainErrorControl(true, "Got unexpected response from server: " + response.status);
            }
        }

#if UNITYBUILD
        private async Task<object> Get<T>(string urlBase, string path, bool raw = false)
        {
            var request = UnityWebRequest.Get(urlBase + path);

            await request.SendWebRequest();

            if (request.isNetworkError || request.isHttpError)
            {
                return new HTTPStatusResponse("exception", request.error);
            }
            else
            {
                if (raw)
                {
                    return request.downloadHandler.text;
                }
                else
                {
                    return JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                }
            }
        }

        private async Task<object> Post<T>(string urlBase, string path, string jsonString)
        {
            var request = new UnityWebRequest(urlBase + path, "POST");            
            byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonString);

            request.uploadHandler = (UploadHandler)new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");  

            UnityEngine.Debug.Log("Before request");
            await request.SendWebRequest();
            UnityEngine.Debug.Log("After request");
            UnityEngine.Debug.Log("Status " + request.isDone);

            if (request.isNetworkError || request.isHttpError)
            {
                return new HTTPStatusResponse("exception", request.error);
            }
            else
            {
                return JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
            }
        }
#else
        private async Task<object> Get<T>(string urlBase, string path, bool raw = false)
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest) WebRequest.Create(urlBase + path);
                request.Timeout = RequestTimeout;

                var responseText = "";
                using(HttpWebResponse response = (HttpWebResponse) await request.GetResponseAsync())
                using(Stream stream = response.GetResponseStream())
                using(StreamReader reader = new StreamReader(stream))
                {
                    responseText = await reader.ReadToEndAsync();
                }

                if (raw)
                {
                    return responseText;
                }
                else
                {
                    return JsonConvert.DeserializeObject<T>(responseText);
                }
            }
            catch (Exception e)
            {
                return new HTTPStatusResponse("exception", e.Message);
            }
        }

        private async Task<object> Post<T>(string urlBase, string path, string jsonString)
        {
            try
            {
                var request = (HttpWebRequest) WebRequest.Create(urlBase + path);
                request.ContentType = "application/json";
                request.Method = "POST";
                request.Timeout = RequestTimeout;

                using (var streamWriter = new StreamWriter(request.GetRequestStream()))
                {
                    streamWriter.Write(jsonString);
                }

                var responseString = "";
                using (var response = (HttpWebResponse) await request.GetResponseAsync())
                using (var streamReader = new StreamReader(response.GetResponseStream()))
                {
                    responseString = await streamReader.ReadToEndAsync();
                }

                return JsonConvert.DeserializeObject<T>(responseString);
            }
            catch (Exception e)
            {
                return new HTTPStatusResponse("exception", e.Message);
            }
        }
#endif

        private void ValidateMessageHash(string messageHash)
        {
            if (messageHash == null)
            {
                throw new Exception("messageHash is not a Buffer");
            }

            if (messageHash.Length != 64)
            {
                throw new Exception("expected length 64 of messageHash, but got " + messageHash.Length);
            }
        }
    }
}