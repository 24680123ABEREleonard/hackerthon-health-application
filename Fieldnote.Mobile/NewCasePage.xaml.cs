using Microsoft.Data.Sqlite;
using Fieldnote.Mobile.Services;

namespace Fieldnote.Mobile;

public partial class NewCasePage : ContentPage
{
    private readonly CaseDatabase _database;

    public NewCasePage(CaseDatabase database)
    {
        InitializeComponent();
        _database = database;
        TypePicker.ItemsSource = new[] { "X-ray", "Ultrasound", "MRI", "CT scan", "Other" };
        PriorityPicker.ItemsSource = new[] { "Routine", "Urgent", "Time-sensitive" };
        TypePicker.SelectedIndex = 0;
        PriorityPicker.SelectedIndex = 0;
    }

    private async void OnChooseImageClicked(object? sender, EventArgs e)
    {
        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Choose an image for local preview",
                FileTypes = FilePickerFileType.Images
            });
            if (file is null)
                return;

            await using var input = await file.OpenReadAsync();
            using var imageStream = new MemoryStream();
            var buffer = new byte[81920];
            var totalBytes = 0;
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > 12 * 1024 * 1024)
                {
                    await DisplayAlertAsync("Image too large", "Choose an image smaller than 12 MB.", "OK");
                    return;
                }
                await imageStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            }

            var imageBytes = imageStream.ToArray();
            ImagePreview.Source = ImageSource.FromStream(() => new MemoryStream(imageBytes, writable: false));
            ImagePreview.IsVisible = true;
            ImageDisclaimer.Text = $"{file.FileName} · previewed only; image is not stored or uploaded.";
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Image unavailable", exception.Message, "OK");
        }
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var id = ReferenceEntry.Text?.Trim() ?? "";
        if (id.Length is < 1 or > 32)
        {
            await DisplayAlertAsync("Check case reference", "Enter a reference between 1 and 32 characters.", "OK");
            return;
        }
        if (TypePicker.SelectedItem is not string type || PriorityPicker.SelectedItem is not string priority)
        {
            await DisplayAlertAsync("Missing selection", "Choose an imaging type and priority.", "OK");
            return;
        }

        try
        {
            await _database.AddCaseAsync(id, type, priority, NotesEditor.Text ?? "");
            await Navigation.PopModalAsync();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            await DisplayAlertAsync("Duplicate case", "That case reference is already in the queue.", "OK");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Could not save case", exception.Message, "OK");
        }
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
