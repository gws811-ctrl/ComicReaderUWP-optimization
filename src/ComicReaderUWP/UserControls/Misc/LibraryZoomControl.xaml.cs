// Copyright (c) aicd0. All rights reserved.
// Licensed under the MIT License.

using System;

using ComicReaderUWP.Common.BaseUI;
using ComicReaderUWP.Data.Models.Misc;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace ComicReaderUWP.UserControls.Misc;

/// <summary>
/// Slider that controls the card size of a library list. The value is shared by every list page
/// (library, collection, search) and persisted in the app settings.
/// </summary>
internal sealed partial class LibraryZoomControl : BaseUserControl
{
    public delegate void ScaleChangedEventHandler(double scale);
    public event ScaleChangedEventHandler? ScaleChanged;

    private DispatcherQueueTimer? _saveTimer;
    private bool _initialized;

    public LibraryZoomControl()
    {
        InitializeComponent();

        ScaleSlider.Minimum = AppSettingsModel.MinLibraryItemScale;
        ScaleSlider.Maximum = AppSettingsModel.MaxLibraryItemScale;

        Loaded += LibraryZoomControl_Loaded;
    }

    private void LibraryZoomControl_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        double scale = AppSettingsModel.LibraryItemScale;
        if (Math.Abs(ScaleSlider.Value - scale) < 0.001)
        {
            ScaleChanged?.Invoke(scale);
        }
        else
        {
            // Raises ValueChanged, which applies and (debounced) persists the value.
            ScaleSlider.Value = scale;
        }
    }

    private void ScaleSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        ScaleChanged?.Invoke(e.NewValue);

        // Persist once the user stops dragging instead of on every intermediate value.
        if (_saveTimer is null)
        {
            _saveTimer = DispatcherQueue.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => AppSettingsModel.LibraryItemScale = ScaleSlider.Value;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
