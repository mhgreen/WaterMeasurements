// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Controls;
using WaterMeasurements.Contracts.Services;

#nullable enable

namespace WaterMeasurements.Services;

/// <summary>
/// A <see langword="class"/> that implements the <see cref="IDialogService"/> <see langword="interface"/> using UWP APIs.
/// </summary>
public sealed class DialogService : IDialogService
{
    /// <inheritdoc/>
    public Task ShowMessageDialogAsync(string title, string message)
    {
        ContentDialog dialog = new();
        dialog.Title = title;
        dialog.CloseButtonText = "Close";
        dialog.DefaultButton = ContentDialogButton.Close;
        dialog.Content = message;

        return dialog.ShowAsync().AsTask();
    }
}
