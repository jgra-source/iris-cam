//
// Creates (or removes) the "Iris Camera" camera device.
//
// Windows keeps a list of cameras that apps like Teams and Zoom read from.
// This adds ours to that list and points it at our component, which is what
// actually produces the pictures. Run as Administrator - Windows will not let
// an ordinary program add a camera that survives.
//
// Usage:  register-camera.exe            create the camera
//         register-camera.exe /uninstall remove it
//
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfvirtualcamera.h>
#include <cstdio>
#include <cwchar>

#pragma comment(lib, "mfsensorgroup.lib")
#pragma comment(lib, "mfplat.lib")
#pragma comment(lib, "mf.lib")
#pragma comment(lib, "mfuuid.lib")
#pragma comment(lib, "ole32.lib")

static const wchar_t* kClsid       = L"{AD12AC5D-5241-4FF1-92AC-B45CAF2ABA15}";
static const wchar_t* kFriendlyName = L"Iris Camera";

static int Fail(const wchar_t* what, HRESULT hr)
{
    wprintf(L"ERROR: %s failed (0x%08X)\n", what, hr);
    if (hr == E_ACCESSDENIED)
        wprintf(L"       Run this as Administrator.\n");
    return 1;
}

static int Create()
{
    IMFVirtualCamera* cam = nullptr;

    HRESULT hr = MFCreateVirtualCamera(
        MFVirtualCameraType_SoftwareCameraSource,
        MFVirtualCameraLifetime_System,        // survives until removed
        MFVirtualCameraAccess_CurrentUser,
        kFriendlyName,
        kClsid,
        nullptr, 0,
        &cam);
    if (FAILED(hr)) return Fail(L"MFCreateVirtualCamera", hr);

    wprintf(L"Camera created. Starting it...\n");

    hr = cam->Start(nullptr);
    if (FAILED(hr))
    {
        cam->Remove();      // don't leave a half-made camera behind
        cam->Release();
        return Fail(L"IMFVirtualCamera::Start", hr);
    }

    wprintf(L"\"%s\" is now installed.\n", kFriendlyName);
    cam->Release();
    return 0;
}

static int Remove()
{
    // Re-creating with the same identity returns the existing camera, which we
    // can then remove. This is how the platform exposes removal.
    IMFVirtualCamera* cam = nullptr;
    HRESULT hr = MFCreateVirtualCamera(
        MFVirtualCameraType_SoftwareCameraSource,
        MFVirtualCameraLifetime_System,
        MFVirtualCameraAccess_CurrentUser,
        kFriendlyName,
        kClsid,
        nullptr, 0,
        &cam);
    if (FAILED(hr)) return Fail(L"MFCreateVirtualCamera (for removal)", hr);

    hr = cam->Remove();
    cam->Release();
    if (FAILED(hr)) return Fail(L"IMFVirtualCamera::Remove", hr);

    wprintf(L"\"%s\" removed.\n", kFriendlyName);
    return 0;
}

int wmain(int argc, wchar_t* argv[])
{
    HRESULT hr = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(hr)) return Fail(L"CoInitializeEx", hr);

    hr = MFStartup(MF_VERSION);
    if (FAILED(hr)) { CoUninitialize(); return Fail(L"MFStartup", hr); }

    const bool uninstall = (argc == 2 && _wcsicmp(argv[1], L"/uninstall") == 0);
    const int rc = uninstall ? Remove() : Create();

    MFShutdown();
    CoUninitialize();
    return rc;
}
