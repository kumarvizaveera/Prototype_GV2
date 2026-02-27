using System;
using System.Reflection;
using Thirdweb.Unity;
using UnityEditor;
using UnityEngine;

namespace Thirdweb.Editor
{
    public abstract class ThirdwebManagerBaseEditor<T> : UnityEditor.Editor
        where T : MonoBehaviour
    {
        protected SerializedProperty InitializeOnAwakeProp;
        protected SerializedProperty InitializeOnStartProp;
        protected SerializedProperty ShowDebugLogsProp;
        protected SerializedProperty AutoConnectLastWalletProp;
        protected SerializedProperty RedirectPageHtmlOverrideProp;
        protected SerializedProperty RpcOverridesProp;

        protected int SelectedTab;
        protected GUIStyle ButtonStyle;
        protected Texture2D BannerImage;

        protected virtual string[] TabTitles => new string[] { "Client/Server", "Preferences", "Misc", "Debug" };

        protected virtual void OnEnable()
        {
            this.InitializeOnAwakeProp = this.FindProp("InitializeOnAwake");
            this.InitializeOnStartProp = this.FindProp("InitializeOnStart");
            this.ShowDebugLogsProp = this.FindProp("ShowDebugLogs");
            this.AutoConnectLastWalletProp = this.FindProp("AutoConnectLastWallet");
            this.RedirectPageHtmlOverrideProp = this.FindProp("RedirectPageHtmlOverride");
            this.RpcOverridesProp = this.FindProp("RpcOverrides");

            this.BannerImage = Resources.Load<Texture2D>("EditorBanner");
        }

        public override void OnInspectorGUI()
        {
            this.serializedObject.Update();

            if (this.ButtonStyle == null)
            {
                this.InitializeStyles();
            }

            this.DrawBannerAndTitle();
            this.DrawTabs();
            GUILayout.Space(10);
            this.DrawSelectedTabContent();

            _ = this.serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawSelectedTabContent()
        {
            switch (this.SelectedTab)
            {
                case 0:
                    this.DrawClientOrServerTab();
                    break;
                case 1:
                    this.DrawPreferencesTab();
                    break;
                case 2:
                    this.DrawMiscTab();
                    break;
                case 3:
                    this.DrawDebugTab();
                    break;
                default:
                    GUILayout.Label("Unknown Tab", EditorStyles.boldLabel);
                    break;
            }
        }

        protected abstract void DrawClientOrServerTab();

        protected virtual void DrawPreferencesTab()
        {
            EditorGUILayout.HelpBox("Set your preferences and initialization options here.", MessageType.Info);
            this.DrawProperty(this.InitializeOnAwakeProp, "Initialize On Awake");
            this.DrawProperty(this.InitializeOnStartProp, "Initialize On Start");
            this.DrawProperty(this.ShowDebugLogsProp, "Show Debug Logs");
            this.DrawProperty(this.AutoConnectLastWalletProp, "Auto-Connect Last Wallet");
        }

        protected virtual void DrawMiscTab()
        {
            EditorGUILayout.HelpBox("Configure other settings here.", MessageType.Info);
            this.DrawProperty(this.RpcOverridesProp, "RPC Overrides");
            GUILayout.Space(10);
            EditorGUILayout.LabelField("OAuth Redirect Page HTML Override", EditorStyles.boldLabel);
            this.RedirectPageHtmlOverrideProp.stringValue = EditorGUILayout.TextArea(this.RedirectPageHtmlOverrideProp.stringValue, GUILayout.MinHeight(150));
        }

        protected virtual void DrawDebugTab()
        {
            EditorGUILayout.HelpBox("Debug your settings here.", MessageType.Info);
            this.DrawButton(
                "Log Active Wallet Info",
                () =>
                {
                    if (!Application.isPlaying)
                    {
                        Debug.LogWarning("Debugging can only be done in Play Mode.");
                        return;
                    }

                    if (this.target is ThirdwebManagerBase manager)
                    {
                        var wallet = manager.ActiveWallet;
                        if (wallet != null)
                        {
                            Debug.Log($"Active Wallet ({wallet.GetType().Name}) Address: {wallet.GetAddress().Result}");
                        }
                        else
                        {
                            Debug.LogWarning("No active wallet found.");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("Active wallet information unavailable for this target.");
                    }
                }
            );
            this.DrawButton(
                "Disconnect Active Wallet",
                () =>
                {
                    if (!Application.isPlaying)
                    {
                        Debug.LogWarning("Debugging can only be done in Play Mode.");
                        return;
                    }

                    if (this.target is ThirdwebManagerBase manager)
                    {
                        EditorApplication.delayCall += async () =>
                        {
                            await manager.DisconnectWallet();
                            Debug.Log("Active wallet disconnected.");
                        };
                    }
                    else
                    {
                        Debug.LogWarning("Active wallet information unavailable for this target.");
                    }
                }
            );
            this.DrawButton(
                "Open Documentation",
                () =>
                {
                    Application.OpenURL("http://portal.thirdweb.com/unity");
                }
            );
        }

        protected void DrawBannerAndTitle()
        {
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (this.BannerImage != null)
            {
                GUILayout.Label(this.BannerImage, GUILayout.Width(64), GUILayout.Height(64));
            }
            GUILayout.Space(10);
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            GUILayout.Label("Thirdweb Configuration", EditorStyles.boldLabel);
            GUILayout.Label("Configure your settings and preferences.\nYou can access ThirdwebManager.Instance from anywhere.", EditorStyles.miniLabel);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.EndVertical();
        }

        protected void DrawTabs()
        {
            this.SelectedTab = GUILayout.Toolbar(this.SelectedTab, this.TabTitles, GUILayout.Height(25));
        }

        protected void InitializeStyles()
        {
            this.ButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 10, 10),
            };
        }

        protected void DrawProperty(SerializedProperty property, string label)
        {
            if (property != null)
            {
                _ = EditorGUILayout.PropertyField(property, new GUIContent(label));
            }
            else
            {
                EditorGUILayout.HelpBox($"Property '{label}' not found.", MessageType.Error);
            }
        }

        protected void DrawButton(string label, Action action)
        {
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(label, this.ButtonStyle, GUILayout.Height(35), GUILayout.ExpandWidth(true)))
            {
                action.Invoke();
            }
            GUILayout.FlexibleSpace();
        }

        protected SerializedProperty FindProp(string propName)
        {
            var targetType = this.target.GetType();
            var property = targetType.GetProperty(propName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (property == null)
            {
                return null;
            }

            var backingFieldName = $"<{propName}>k__BackingField";
            return this.serializedObject.FindProperty(backingFieldName);
        }
    }

    [CustomEditor(typeof(ThirdwebManager))]
    public class ThirdwebManagerEditor : ThirdwebManagerBaseEditor<ThirdwebManager>
    {
        private SerializedProperty _clientIdProp;
        private SerializedProperty _bundleIdProp;

        protected override void OnEnable()
        {
            base.OnEnable();
            this._clientIdProp = this.FindProp("ClientId");
            this._bundleIdProp = this.FindProp("BundleId");
        }

        protected override string[] TabTitles => new string[] { "Client", "Preferences", "Misc", "Debug" };

        protected override void DrawClientOrServerTab()
        {
            EditorGUILayout.HelpBox("Configure your client settings here.", MessageType.Info);
            this.DrawProperty(this._clientIdProp, "Client ID");
            this.DrawProperty(this._bundleIdProp, "Bundle ID");
            this.DrawButton(
                "Create API Key",
                () =>
                {
                    Application.OpenURL("https://thirdweb.com/create-api-key");
                }
            );
        }
    }

    [CustomEditor(typeof(ThirdwebManagerServer))]
    public class ThirdwebManagerServerEditor : ThirdwebManagerBaseEditor<ThirdwebManagerServer>
    {
        private SerializedProperty _secretKeyProp;

        protected override void OnEnable()
        {
            base.OnEnable();
            this._secretKeyProp = this.FindProp("SecretKey");
        }

        protected override string[] TabTitles => new string[] { "Client", "Preferences", "Misc", "Debug" };

        protected override void DrawClientOrServerTab()
        {
            EditorGUILayout.HelpBox("Configure your client settings here.", MessageType.Info);
            this.DrawProperty(this._secretKeyProp, "Secret Key");
            this.DrawButton(
                "Create API Key",
                () =>
                {
                    Application.OpenURL("https://thirdweb.com/create-api-key");
                }
            );
        }
    }
}
