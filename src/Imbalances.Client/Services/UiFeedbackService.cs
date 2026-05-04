using System;
using System.Threading.Tasks;
using MudBlazor;
using Microsoft.JSInterop;

namespace Imbalances.Client.Services;

public sealed class UiFeedbackService
{
    private readonly ISnackbar _snackbar;
    private readonly IJSRuntime _jsRuntime;

    public UiFeedbackService(ISnackbar snackbar, IJSRuntime jsRuntime)
    {
        _snackbar = snackbar;
        _jsRuntime = jsRuntime;
    }

    public void ShowSuccess(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _snackbar.Add(message, Severity.Success);
    }

    public void ShowInfo(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _snackbar.Add(message, Severity.Info);
    }

    public void ShowWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _snackbar.Add(message, Severity.Warning);
    }

    public void ShowError(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _snackbar.Add(message, Severity.Error);
    }

    public void ShowError(Exception ex, string fallbackMessage)
    {
        var message = ex?.Message;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = fallbackMessage;
        }

        ShowError(message);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "Confirmar", string cancelText = "Cancelar")
    {
        try
        {
            var payload = string.IsNullOrWhiteSpace(title) ? message : $"{title}\n\n{message}";
            return await _jsRuntime.InvokeAsync<bool>("confirm", payload);
        }
        catch
        {
            return false;
        }
    }
}
