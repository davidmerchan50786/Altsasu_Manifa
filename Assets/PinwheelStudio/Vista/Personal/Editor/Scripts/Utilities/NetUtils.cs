#if VISTA
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Networking;
using Unity.EditorCoroutines.Editor;
using System;
using System.Text;
using Pinwheel.Vista;

namespace Pinwheel.VistaEditor
{
    public static class NetUtils
    {
        public static string _ModURL(string url, string utmMedium = "")
        {
            const string PINWHEELSTUDIO = "pinwheelstud.io";
            const string ASSETSTOREUNITYCOM = "assetstore.unity.com";

            string utmCampaign = "";
            string utmSource =
                EditorCommon.IsProEdition() ? "vista-editor-pro" :
                EditorCommon.IsIndieEdition() ? "vista-editor-indie" :
                "vista-editor-psn";

            if (url.Contains(PINWHEELSTUDIO) ||
                url.Contains(ASSETSTOREUNITYCOM))
            {
                string queryString = "";
                int queryStart = url.IndexOf('?');
                if (queryStart > 0)
                {
                    queryString = url.Substring(queryStart).Remove(0, 1);
                    url = url.Remove(queryStart);
                }

                Dictionary<string, string> queries = new Dictionary<string, string>();
                ParseQuery(queryString, queries);

                if (url.Contains(PINWHEELSTUDIO))
                {
                    queries["utm_campaign"] = utmCampaign;
                    queries["utm_source"] = utmSource;
                    queries["utm_medium"] = utmMedium;
                }
                //else if (url.Contains(ASSETSTOREUNITYCOM))
                //{
                //    queries["aid"] = AFF_ID;
                //    queries["pubref"] = $"{utmCampaign}_{utmSource}_{utmMedium}";
                //}

                url = CombinePathAndQuery(url, queries);
                return url;
            }
            else
            {
                return url;
            }
        }

        private static void ParseQuery(string queryString, Dictionary<string, string> pairs)
        {
            string[] elements = queryString.Split('=', '&');
            int numPair = elements.Length / 2;
            for (int i = 0; i < numPair; ++i)
            {
                string key = elements[i * 2 + 0];
                string value = elements[i * 2 + 1];
                pairs[key] = value;
            }
        }

        private static string CombinePathAndQuery(string url, Dictionary<string, string> queries)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(url).Append('?');
            foreach (var pair in queries)
            {
                sb.Append(pair.Key).Append('=').Append(pair.Value).Append('&');
            }
            return sb.ToString();
        }

        public static void TrackClick(string button_name, UILocation location)
        {
            const string ENDPOINT_URL = "https://api.pinwheelstud.io/pwi/editor/btn-click/";

            string buttonId = $"{button_name.ToLower().Replace(" ", "_")}__{location.ToString().ToLower()}";
            if (string.IsNullOrEmpty(buttonId))
                return;

            var payload =
                "{\"product\":\"" + Escape(VersionInfo.productName) +
                "\",\"button_id\":\"" + Escape(buttonId) + "\"}";

            var request = new UnityWebRequest(ENDPOINT_URL, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload)),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.disposeUploadHandlerOnDispose = true;
            request.disposeDownloadHandlerOnDispose = true;

            request.SetRequestHeader("Content-Type", "application/json");
            var ops = request.SendWebRequest();
            ops.completed += _ => { request.Dispose(); };
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
#endif