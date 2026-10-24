using Fieldnote.Mobile.Models;
using Fieldnote.Mobile.Services;

namespace Fieldnote.Mobile;

public partial class MainPage : ContentPage
{
    private readonly CaseDatabase _database;
    private readonly WorkflowAssistant _assistant;
    private IReadOnlyList<ImagingCase> _cases = [];
    private string _searchText = "";
    private string _statusFilter = "All";
    private string _view = "Home";
    private bool _isInitialized;
    private VerticalStackLayout? _caseResults;
    private Label? _caseCount;

    public MainPage(CaseDatabase database, WorkflowAssistant assistant)
    {
        InitializeComponent();
        _database = database;
        _assistant = assistant;
        NavigationPage.SetHasNavigationBar(this, false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_isInitialized)
        {
            try
            {
                await _database.InitializeAsync();
                _isInitialized = true;
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Database unavailable", exception.Message, "OK");
                return;
            }
        }

        try
        {
            await RefreshAsync();
            ShowCurrentView();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Could not load local cases", exception.Message, "OK");
        }
    }

    private async Task RefreshAsync()
    {
        _cases = await _database.GetCasesAsync();
    }

    private void ShowCurrentView()
    {
        PageBody.Children.Clear();
        switch (_view)
        {
            case "Cases":
                BuildCases();
                break;
            case "AI help":
                BuildAssistant();
                break;
            case "Settings":
                BuildSettings();
                break;
            default:
                BuildDashboard();
                break;
        }
    }

    private void BuildDashboard()
    {
        var today = _cases.Count(item => item.IsToday);
        var pending = _cases.Count(item => item.Status == "Needs review");
        var reviewed = _cases.Count(item => item.IsToday && item.Status == "Reviewed");

        PageBody.Children.Add(Eyebrow(DateTime.Now.ToString("dddd, d MMMM yyyy").ToUpperInvariant()));
        PageBody.Children.Add(Heading("Good morning, Alex"));
        PageBody.Children.Add(Subtitle("Your clinic’s imaging review queue."));
        PageBody.Children.Add(Notice(_assistant.IsConfigured
            ? $"Optional local AI is connected ({_assistant.ModelName}) for software help only. It does not analyze images or provide medical advice."
            : "Optional local AI can answer software questions after setup. No clinical AI or image analysis is connected."));
        var metrics = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) },
            ColumnSpacing = 10,
            RowSpacing = 10
        };
        metrics.Add(MetricCard("Cases today", today.ToString("00"), "#E9F1F8"), 0, 0);
        metrics.Add(MetricCard("Needs review", pending.ToString("00"), "#FBF2DF"), 1, 0);
        metrics.Add(MetricCard("Reviewed today", reviewed.ToString("00"), "#E6F3EF"), 0, 1);
        metrics.Add(MetricCard("Database", "SQLite", "#F0EDFA"), 1, 1);
        PageBody.Children.Add(metrics);

        PageBody.Children.Add(SectionTitle("Recent cases", "Your latest clinic records"));
        foreach (var item in _cases.Take(4))
            PageBody.Children.Add(CaseCard(item));

        PageBody.Children.Add(Notice("Case records stay in SQLite on this device. Do not enter real patient information."));
        PageBody.Children.Add(ActionButton("+  New imaging case", OnNewCaseClicked));
    }

    private void BuildCases()
    {
        PageBody.Children.Add(Eyebrow("LOCAL DATABASE"));
        PageBody.Children.Add(Heading("Case queue"));
        PageBody.Children.Add(Subtitle("Search cases and record clinician review."));
        var search = new SearchBar
        {
            Placeholder = "Search cases",
            Text = _searchText,
            BackgroundColor = Colors.White
        };
        search.TextChanged += (_, args) =>
        {
            _searchText = args.NewTextValue ?? "";
            RenderCaseResults();
        };
        PageBody.Children.Add(search);

        var filter = new Picker { Title = "Filter cases", BackgroundColor = Colors.White };
        filter.Items.Add("All");
        filter.Items.Add("Needs review");
        filter.Items.Add("Reviewed");
        filter.SelectedItem = _statusFilter;
        filter.SelectedIndexChanged += (_, _) =>
        {
            _statusFilter = filter.SelectedItem?.ToString() ?? "All";
            RenderCaseResults();
        };
        PageBody.Children.Add(filter);
        _caseCount = new Label { FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#334248") };
        PageBody.Children.Add(_caseCount);
        PageBody.Children.Add(Subtitle("Stored on this phone only"));
        _caseResults = new VerticalStackLayout { Spacing = 10 };
        PageBody.Children.Add(_caseResults);
        RenderCaseResults();
        PageBody.Children.Add(ActionButton("+  New imaging case", OnNewCaseClicked));
    }

    private void RenderCaseResults()
    {
        if (_caseResults is null || _caseCount is null)
            return;

        var matches = _cases.Where(item =>
            (_statusFilter == "All" || item.Status == _statusFilter) &&
            $"{item.Id} {item.Type} {item.Detail} {item.Priority} {item.Status}"
                .Contains(_searchText, StringComparison.OrdinalIgnoreCase)).ToList();
        _caseCount.Text = $"{matches.Count} {(matches.Count == 1 ? "case" : "cases")}";
        _caseResults.Children.Clear();
        if (matches.Count == 0)
            _caseResults.Children.Add(Subtitle("No cases match your search."));
        foreach (var item in matches)
            _caseResults.Children.Add(CaseCard(item));
    }

    private void BuildAssistant()
    {
        PageBody.Children.Add(Eyebrow("SOFTWARE HELP ONLY"));
        PageBody.Children.Add(Heading("Workflow assistant"));
        PageBody.Children.Add(Subtitle("Optional local AI for app questions. No case data or images are sent."));
        PageBody.Children.Add(Notice(_assistant.IsConfigured
            ? $"Local model ready: {_assistant.ModelName}. Clinical questions are not supported."
            : "AI is not configured. Add a local Ollama server and model in Settings."));

        AddAssistantTopic("Adding a case", "Required fields and local image selection", "add-case");
        AddAssistantTopic("Recording clinician review", "Change a case’s workflow status", "review-case");
        AddAssistantTopic("Finding a case", "Search and filter the local queue", "find-case");
        AddAssistantTopic("Storage and privacy", "Where this demo keeps data", "privacy");

        var answer = new Label
        {
            Text = "Choose a software-help topic above.",
            TextColor = Color.FromArgb("#526168"),
            FontSize = 14,
            LineBreakMode = LineBreakMode.WordWrap
        };
        var answerCard = Card("Workflow help", answer);
        PageBody.Children.Add(answerCard);
        answerCard.AutomationId = "assistant-answer-card";
        _assistantAnswerLabel = answer;
    }

    private Label? _assistantAnswerLabel;

    private void AddAssistantTopic(string title, string description, string topic)
    {
        var button = new Button
        {
            Text = $"{title}\n{description}",
            BackgroundColor = Colors.White,
            TextColor = Color.FromArgb("#38564E"),
            BorderColor = Color.FromArgb("#E6EEEA"),
            BorderWidth = 1,
            CornerRadius = 10,
            Padding = new Thickness(14, 12),
            FontSize = 13
        };
        button.Clicked += async (_, _) =>
        {
            if (_assistantAnswerLabel is null)
                return;

            _assistantAnswerLabel.Text = "Contacting the local model…";
            try
            {
                _assistantAnswerLabel.Text = await _assistant.AskAsync(topic);
            }
            catch (Exception exception)
            {
                _assistantAnswerLabel.Text = exception.Message;
            }
        };
        PageBody.Children.Add(button);
    }

    private void BuildSettings()
    {
        PageBody.Children.Add(Eyebrow("ON THIS DEVICE"));
        PageBody.Children.Add(Heading("Settings & privacy"));
        PageBody.Children.Add(Subtitle("Cases are saved in an unencrypted SQLite file in this app’s private storage."));
        PageBody.Children.Add(SectionTitle("Local AI model", "Optional · software help only"));

        var serverEntry = new Entry
        {
            Text = _assistant.ServerAddress,
            Placeholder = "https://your-private-model-host",
            Keyboard = Keyboard.Url,
            BackgroundColor = Colors.White
        };
        var modelEntry = new Entry
        {
            Text = _assistant.ModelName,
            Placeholder = "Ollama model name (e.g. llama3.2)",
            BackgroundColor = Colors.White
        };
        PageBody.Children.Add(FormLabel("Ollama server address"));
        PageBody.Children.Add(serverEntry);
        PageBody.Children.Add(FormLabel("Model name"));
        PageBody.Children.Add(modelEntry);
        PageBody.Children.Add(Subtitle("For the Android emulator, Ollama on the host may be reachable at http://10.0.2.2:11434. For a phone on Wi-Fi, use a private HTTPS endpoint. Only preset app-help prompts are sent."));
        PageBody.Children.Add(ActionButton("Save AI settings", async () =>
        {
            try
            {
                _assistant.SaveConfiguration(serverEntry.Text ?? "", modelEntry.Text ?? "");
                await DisplayAlertAsync("Saved", _assistant.IsConfigured
                    ? "Local workflow assistant configured."
                    : "AI remains disabled until you enter a model name.", "OK");
            }
            catch (ArgumentException exception)
            {
                await DisplayAlertAsync("Invalid address", exception.Message, "OK");
            }
        }));

        PageBody.Children.Add(SectionTitle("Case data", "SQLite database on this phone"));
        PageBody.Children.Add(Subtitle(_database.DatabasePath));
        PageBody.Children.Add(ActionButton("Clear all local cases", async () =>
        {
            var confirmed = await DisplayAlertAsync("Clear case database?", "This permanently deletes all demo cases and audit records on this phone.", "Clear", "Cancel");
            if (!confirmed)
                return;
            try
            {
                await _database.ClearCasesAsync();
                await RefreshAsync();
                ShowCurrentView();
            }
            catch (Exception exception)
            {
                await DisplayAlertAsync("Could not clear cases", exception.Message, "OK");
            }
        }, "#A04444"));

        PageBody.Children.Add(Notice("For demonstration only. Do not use real patient information or rely on this app for clinical decisions."));
    }

    private View CaseCard(ImagingCase item)
    {
        var statusColor = item.Status == "Reviewed" ? "#52806C" : "#A27838";
        var detail = new VerticalStackLayout
        {
            Spacing = 5,
            Children =
            {
                new Label { Text = item.Id, FontSize = 14, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#354449") },
                new Label { Text = $"{item.Type}  ·  {item.Detail}", FontSize = 11, TextColor = Color.FromArgb("#849194") },
                new Label { Text = $"{item.Priority}  ·  {item.Status}  ·  {item.AddedLabel}", FontSize = 10, TextColor = Color.FromArgb(statusColor) }
            }
        };
        var button = new Button
        {
            Text = "Open",
            BackgroundColor = Color.FromArgb("#EAF3EE"),
            TextColor = Color.FromArgb("#3F7B65"),
            CornerRadius = 7,
            Padding = new Thickness(10, 5),
            FontSize = 11,
            VerticalOptions = LayoutOptions.Center
        };
        button.Clicked += async (_, _) => await OpenCaseAsync(item);
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 10
        };
        grid.Add(detail, 0, 0);
        grid.Add(button, 1, 0);
        return Card("", grid);
    }

    private async Task OpenCaseAsync(ImagingCase item)
    {
        var action = await DisplayActionSheetAsync(
            $"{item.Id} · {item.Type}\n{item.Detail}\nPriority: {item.Priority}\nStatus: {item.Status}\n\n{item.Notes}",
            "Close",
            null,
            item.Status == "Reviewed" ? "Return to review queue" : "Mark clinician reviewed");
        if (action is not ("Mark clinician reviewed" or "Return to review queue"))
            return;

        var status = action == "Mark clinician reviewed" ? "Reviewed" : "Needs review";
        try
        {
            await _database.SetReviewStatusAsync(item.Id, status);
            await RefreshAsync();
            ShowCurrentView();
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Could not update case", exception.Message, "OK");
        }
    }

    private async Task OnNewCaseClicked()
    {
        await Navigation.PushModalAsync(new NavigationPage(new NewCasePage(_database)));
    }

    private void OnHomeClicked(object? sender, EventArgs e) => Navigate("Home");
    private void OnCasesClicked(object? sender, EventArgs e) => Navigate("Cases");
    private void OnAssistantClicked(object? sender, EventArgs e) => Navigate("AI help");
    private void OnSettingsClicked(object? sender, EventArgs e) => Navigate("Settings");

    private void Navigate(string view)
    {
        _view = view;
        ShowCurrentView();
    }

    private static Label Eyebrow(string text) => new()
    {
        Text = text,
        FontSize = 9,
        CharacterSpacing = 1,
        TextColor = Color.FromArgb("#82918E")
    };

    private static Label Heading(string text) => new()
    {
        Text = text,
        FontSize = 27,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#27373D")
    };

    private static Label Subtitle(string text) => new()
    {
        Text = text,
        FontSize = 12,
        TextColor = Color.FromArgb("#829094"),
        LineBreakMode = LineBreakMode.WordWrap
    };

    private static Label FormLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        TextColor = Color.FromArgb("#566469")
    };

    private static View SectionTitle(string title, string caption) => new VerticalStackLayout
    {
        Spacing = 3,
        Children =
        {
            new Label { Text = title, FontSize = 16, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#334248") },
            new Label { Text = caption, FontSize = 11, TextColor = Color.FromArgb("#8B9798") }
        }
    };

    private static View MetricCard(string title, string value, string tint) => Card("", new VerticalStackLayout
    {
        Spacing = 9,
        Children =
        {
            new Label { Text = title, FontSize = 11, TextColor = Color.FromArgb("#748286") },
            new Label { Text = value, FontSize = 25, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#2C3C41") }
        }
    }, tint);

    private static View Notice(string text) => Card("", new Label
    {
        Text = text,
        FontSize = 11,
        TextColor = Color.FromArgb("#806D43"),
        LineBreakMode = LineBreakMode.WordWrap
    }, "#FBF8EF");

    private static Border Card(string title, View content, string background = "White")
    {
        var stack = new VerticalStackLayout { Spacing = 8 };
        if (!string.IsNullOrWhiteSpace(title))
            stack.Add(new Label { Text = title.ToUpperInvariant(), FontSize = 9, CharacterSpacing = 1, TextColor = Color.FromArgb("#81908C") });
        stack.Add(content);
        return new Border
        {
            Stroke = Color.FromArgb("#E7EEEA"),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            BackgroundColor = background == "White" ? Colors.White : Color.FromArgb(background),
            Padding = 13,
            Content = stack
        };
    }

    private static Button ActionButton(string text, Func<Task> action, string color = "#1E7565")
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(color),
            TextColor = Colors.White,
            CornerRadius = 8,
            Padding = new Thickness(14, 12),
            FontAttributes = FontAttributes.Bold,
            FontSize = 13
        };
        button.Clicked += async (_, _) => await action();
        return button;
    }
}
