using System.Linq;
using System.Numerics;
using UnityEngine;

namespace Thirdweb.Unity
{
    public class ThirdwebManager : ThirdwebManagerBase
    {
        [field: SerializeField]
        private string ClientId { get; set; }

        [field: SerializeField]
        private string BundleId { get; set; }

        public static new ThirdwebManager Instance => ThirdwebManagerBase.Instance as ThirdwebManager;

        protected override ThirdwebClient CreateClient()
        {
            if (string.IsNullOrWhiteSpace(this.ClientId))
            {
                ThirdwebDebug.LogError("ClientId must be set in order to initialize ThirdwebManager. " + "Get your API key from https://thirdweb.com/create-api-key");
                return null;
            }

            if (string.IsNullOrWhiteSpace(this.BundleId))
            {
                this.BundleId = null;
            }

            this.BundleId ??= string.IsNullOrWhiteSpace(Application.identifier) ? $"com.{Application.companyName}.{Application.productName}" : Application.identifier;

            return ThirdwebClient.Create(
                clientId: this.ClientId,
                bundleId: this.BundleId,
                httpClient: new CrossPlatformUnityHttpClient(),
                sdkName: Application.platform == RuntimePlatform.WebGLPlayer ? "UnitySDK_WebGL" : "UnitySDK",
                sdkOs: Application.platform.ToString(),
                sdkPlatform: "unity",
                sdkVersion: THIRDWEB_UNITY_SDK_VERSION,
                rpcOverrides: (this.RpcOverrides == null || this.RpcOverrides.Count == 0)
                    ? null
                    : this.RpcOverrides.ToDictionary(rpcOverride => new BigInteger(rpcOverride.ChainId), rpcOverride => rpcOverride.RpcUrl)
            );
        }

        public override string MobileRedirectScheme => this.BundleId + "://";
    }
}
