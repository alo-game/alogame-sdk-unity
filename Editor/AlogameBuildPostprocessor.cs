using System.IO;
using System.Xml;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

#if UNITY_ANDROID
using UnityEditor.Android;
#endif

#if UNITY_IOS
using UnityEditor.iOS.Xcode;
#endif

namespace Alogame.SDK.Editor
{
    /// <summary>
    /// Injects the Android manifest / iOS Info.plist entries the native SDK's optional social
    /// login providers (Facebook, TikTok) need, driven by an <see cref="AlogameSDKSettings"/>
    /// asset (Resources/AlogameSDKSettings). Entries are added conditionally — a game that
    /// doesn't enable a provider gets none of that provider's manifest/plist noise.
    ///
    /// The same settings asset gates the matching Gradle coordinates in
    /// <see cref="AlogameAndroidPostprocessBuild"/>. Both passes must read the same flags:
    /// injecting a provider's manifest entries without its dependency produces a manifest
    /// pointing at classes that are not on the classpath.
    /// </summary>
    public class AlogameBuildPostprocessor
#if UNITY_ANDROID
        : IPostGenerateGradleAndroidProject
#endif
    {
        public int callbackOrder => 0;

        private static AlogameSDKSettings LoadSettings()
        {
            var settings = AlogameSDKSettings.LoadOrNull();
            if (settings == null)
                Debug.LogWarning("[Alogame SDK] No AlogameSDKSettings asset found under a Resources folder — " +
                                  "skipping manifest/plist injection. Create one via Assets > Create > Alogame > SDK Settings " +
                                  "if you use Facebook or TikTok login.");
            return settings;
        }

#if UNITY_ANDROID
        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var settings = LoadSettings();
            if (settings == null || !(settings.enableFacebookLogin || settings.enableTikTokLogin)) return;

            string manifestPath = Path.Combine(path, "src/main/AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning($"[Alogame SDK] Expected AndroidManifest.xml at {manifestPath} — skipping injection.");
                return;
            }

            var doc = new XmlDocument();
            doc.Load(manifestPath);
            var manifestNode = doc.SelectSingleNode("/manifest");
            var applicationNode = doc.SelectSingleNode("/manifest/application");

            var queries = GetOrCreateChild(doc, manifestNode, "queries");
            if (settings.enableTikTokLogin)
            {
                AddPackageQuery(doc, queries, "com.zhiliaoapp.musically");
                AddPackageQuery(doc, queries, "com.ss.android.ugc.trill");
            }
            if (settings.enableFacebookLogin)
            {
                AddPackageQuery(doc, queries, "com.facebook.katana");

                AddMetaData(doc, applicationNode, "com.facebook.sdk.ApplicationId", settings.facebookAppId);
                AddMetaData(doc, applicationNode, "com.facebook.sdk.ClientToken", settings.facebookClientToken);
                AddMetaData(doc, applicationNode, "com.facebook.sdk.AutoInitEnabled", "false");
                AddMetaData(doc, applicationNode, "com.facebook.sdk.AutoLogAppEventsEnabled", "false");

                var facebookActivity = doc.CreateElement("activity");
                SetAndroidAttr(doc, facebookActivity, "name", "com.facebook.FacebookActivity");
                SetAndroidAttr(doc, facebookActivity, "configChanges", "keyboard|keyboardHidden|screenLayout|screenSize|orientation");
                applicationNode.AppendChild(facebookActivity);

                var customTabActivity = doc.CreateElement("activity");
                SetAndroidAttr(doc, customTabActivity, "name", "com.facebook.CustomTabActivity");
                SetAndroidAttr(doc, customTabActivity, "exported", "true");
                var intentFilter = doc.CreateElement("intent-filter");
                AppendAction(doc, intentFilter, "android.intent.action.VIEW");
                AppendCategory(doc, intentFilter, "android.intent.category.DEFAULT");
                AppendCategory(doc, intentFilter, "android.intent.category.BROWSABLE");
                var data = doc.CreateElement("data");
                SetAndroidAttr(doc, data, "scheme", $"fb{settings.facebookAppId}");
                intentFilter.AppendChild(data);
                customTabActivity.AppendChild(intentFilter);
                applicationNode.AppendChild(customTabActivity);
            }
            if (settings.enableTikTokLogin)
            {
                AddMetaData(doc, applicationNode, "TikTokAppId", settings.tikTokClientKey);
            }

            doc.Save(manifestPath);
            Debug.Log("[Alogame SDK] Injected AndroidManifest entries for enabled social providers.");
        }

        private static XmlNode GetOrCreateChild(XmlDocument doc, XmlNode parent, string name)
        {
            var existing = parent.SelectSingleNode(name);
            if (existing != null) return existing;
            var node = doc.CreateElement(name);
            parent.AppendChild(node);
            return node;
        }

        private static void AddPackageQuery(XmlDocument doc, XmlNode queries, string packageName)
        {
            var pkg = doc.CreateElement("package");
            SetAndroidAttr(doc, pkg, "name", packageName);
            queries.AppendChild(pkg);
        }

        private static void AddMetaData(XmlDocument doc, XmlNode application, string name, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            var meta = doc.CreateElement("meta-data");
            SetAndroidAttr(doc, meta, "name", name);
            SetAndroidAttr(doc, meta, "value", value);
            application.AppendChild(meta);
        }

        private static void AppendAction(XmlDocument doc, XmlNode intentFilter, string name)
        {
            var action = doc.CreateElement("action");
            SetAndroidAttr(doc, action, "name", name);
            intentFilter.AppendChild(action);
        }

        private static void AppendCategory(XmlDocument doc, XmlNode intentFilter, string name)
        {
            var category = doc.CreateElement("category");
            SetAndroidAttr(doc, category, "name", name);
            intentFilter.AppendChild(category);
        }

        private static void SetAndroidAttr(XmlDocument doc, XmlElement element, string attr, string value)
        {
            var xmlAttr = doc.CreateAttribute("android", attr, "http://schemas.android.com/apk/res/android");
            xmlAttr.Value = value;
            element.Attributes.Append(xmlAttr);
        }
#endif

        [PostProcessBuild(0)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
#if UNITY_IOS
            if (target != BuildTarget.iOS) return;
            var settings = AlogameSDKSettings.LoadOrNull();

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;

            // NSUserTrackingUsageDescription is written unconditionally, before the
            // social-provider check below, because it is not optional: the SDK requests App
            // Tracking Transparency during analytics init (OegSdkV2 calls
            // requestTrackingAuthorizationWithCompletionHandler:, driven by Adjust). Calling
            // that API with no usage string in Info.plist is a TCC privacy violation, and iOS
            // does not merely deny it — it kills the process. Observed on device as an
            // immediate SIGABRT right after "Analytics initialized from remote config", with
            // __TCC_CRASHING_DUE_TO_PRIVACY_VIOLATION__ on the aborting thread.
            //
            // A game with no settings asset still gets the default string, because a game that
            // uses neither Facebook nor TikTok login still runs Adjust.
            var trackingDescription = settings != null && !string.IsNullOrEmpty(settings.userTrackingUsageDescription)
                ? settings.userTrackingUsageDescription
                : AlogameSDKSettings.DefaultUserTrackingUsageDescription;
            root.SetString("NSUserTrackingUsageDescription", trackingDescription);
            Debug.Log("[Alogame SDK] Set NSUserTrackingUsageDescription (required — the SDK requests ATT).");

            if (settings == null || !(settings.enableFacebookLogin || settings.enableTikTokLogin))
            {
                File.WriteAllText(plistPath, plist.WriteToString());
                return;
            }

            // Dev-convenience default — tighten to explicit NSExceptionDomains before App Store
            // submission if the game's backend hosts don't already have valid TLS certs.
            var ats = root.CreateDict("NSAppTransportSecurity");
            ats.SetBoolean("NSAllowsArbitraryLoads", true);

            if (settings.enableTikTokLogin)
            {
                var queriesSchemes = root.CreateArray("LSApplicationQueriesSchemes");
                queriesSchemes.AddString("tiktokopensdk");
                queriesSchemes.AddString("tiktoksharesdk");
                queriesSchemes.AddString("snssdk1180");
                queriesSchemes.AddString("snssdk1233");

                if (!string.IsNullOrEmpty(settings.tikTokClientKey))
                    root.SetString("TikTokClientKey", settings.tikTokClientKey);
            }

            string urlScheme = settings.enableTikTokLogin ? settings.tikTokClientKey : settings.facebookAppId;
            if (!string.IsNullOrEmpty(urlScheme))
            {
                var urlTypes = root.CreateArray("CFBundleURLTypes");
                var urlTypeDict = urlTypes.AddDict();
                var schemes = urlTypeDict.CreateArray("CFBundleURLSchemes");
                schemes.AddString(urlScheme);
            }

            File.WriteAllText(plistPath, plist.WriteToString());
            Debug.Log("[Alogame SDK] Injected Info.plist entries for enabled social providers.");
#endif
        }
    }
}
