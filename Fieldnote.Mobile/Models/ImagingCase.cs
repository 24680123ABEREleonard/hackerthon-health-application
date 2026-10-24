namespace Fieldnote.Mobile.Models;

public sealed record ImagingCase(
    string Id,
    string Type,
    string Detail,
    string Priority,
    string Status,
    DateTimeOffset CreatedUtc,
    string Notes)
{
    public string AddedLabel
    {
        get
        {
            var local = CreatedUtc.ToLocalTime();
            if (local.Date == DateTime.Now.Date)
                return local.ToString("HH:mm");
            if (local.Date == DateTime.Now.Date.AddDays(-1))
                return "Yesterday";
            return local.ToString("dd MMM yyyy");
        }
    }

    public bool IsToday => CreatedUtc.ToLocalTime().Date == DateTime.Now.Date;
}
