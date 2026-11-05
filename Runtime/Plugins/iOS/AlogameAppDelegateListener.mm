// Forwards app-delegate events Unity's app controller receives to the Alogame SDK bridge
// (AlogameUnityBridge.swift @_cdecl functions). The SDK does not hook the app delegate itself,
// and in a Unity export the app delegate is UnityAppController, so without this:
//   - iOS push never gets a device token (APNs token goes nowhere),
//   - Facebook / TikTok login never start (they need didFinishLaunchingWithOptions:),
//   - social-login callback URLs never reach the SDK (OEGManager.handleOpenURL).
//
// The push token arrives only when Classes/Preprocessor.h has UNITY_USES_REMOTE_NOTIFICATIONS 1;
// AlogameIosPostprocessBuild sets it.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import "AppDelegateListener.h"

extern "C" void AlogameUnity_DidFinishLaunching(NSDictionary* launchOptions);
extern "C" void AlogameUnity_DidRegisterPushToken(const void* bytes, int32_t length);
extern "C" void AlogameUnity_OpenURL(const char* url);

@interface AlogameAppDelegateListener : NSObject<AppDelegateListener>
@end

@implementation AlogameAppDelegateListener

static AlogameAppDelegateListener* sAlogameListener;

+ (void)load
{
    sAlogameListener = [[AlogameAppDelegateListener alloc] init];
    UnityRegisterAppDelegateListener(sAlogameListener);
    // Not part of the AppDelegateListener selectors Unity registers, so observe it directly.
    // Unity posts the raw NSData token as the notification's userInfo.
    [[NSNotificationCenter defaultCenter] addObserver: sAlogameListener
                                             selector: @selector(alogameDidRegisterForRemoteNotifications:)
                                                 name: kUnityDidRegisterForRemoteNotificationsWithDeviceToken
                                               object: nil];
}

- (void)didFinishLaunching:(NSNotification*)notification
{
    NSDictionary* options = [notification.userInfo isKindOfClass: [NSDictionary class]] ? notification.userInfo : nil;
    AlogameUnity_DidFinishLaunching(options);
}

- (void)alogameDidRegisterForRemoteNotifications:(NSNotification*)notification
{
    id token = notification.userInfo;
    if ([token isKindOfClass: [NSData class]])
    {
        NSData* data = (NSData*)token;
        AlogameUnity_DidRegisterPushToken(data.bytes, (int32_t)data.length);
    }
}

- (void)onOpenURL:(NSNotification*)notification
{
    NSURL* url = [notification.userInfo isKindOfClass: [NSDictionary class]] ? notification.userInfo[@"url"] : nil;
    if ([url isKindOfClass: [NSURL class]])
        AlogameUnity_OpenURL(url.absoluteString.UTF8String);
}

@end
