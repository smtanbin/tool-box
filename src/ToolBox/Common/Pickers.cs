// Copyright 2026 Tanbin (www.magestica.com). My Tool Box.
// Licensed under the Apache License 2.0 with the Commons Clause (no selling); see LICENSE.

using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace ToolBox.Pages;

public static class Pickers
{
    static nint Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(App.Window);

    public static async Task<string?> Folder()
    {
        var p = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        p.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(p, Hwnd);
        return (await p.PickSingleFolderAsync())?.Path;
    }

    public static async Task<Windows.Storage.StorageFile?> Open(params string[] types)
    {
        var p = new FileOpenPicker();
        foreach (var t in types) p.FileTypeFilter.Add(t);
        WinRT.Interop.InitializeWithWindow.Initialize(p, Hwnd);
        return await p.PickSingleFileAsync();
    }

    public static async Task<Windows.Storage.StorageFile?> Save(string name, string ext, string label)
    {
        var p = new FileSavePicker { SuggestedFileName = name };
        p.FileTypeChoices.Add(label, new List<string> { ext });
        WinRT.Interop.InitializeWithWindow.Initialize(p, Hwnd);
        return await p.PickSaveFileAsync();
    }
}
