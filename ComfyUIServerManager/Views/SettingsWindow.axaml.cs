using System;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ComfyUIServerManager.Models;
using ComfyUIServerManager.Platform;

namespace ComfyUIServerManager.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _draft;
    private readonly IPlatformServices _platform;
    private TaskCompletionSource<AppSettings?>? _resultTcs;

    // Designer-only no-arg ctor.
    public SettingsWindow() : this(new AppSettings(), PlatformServicesFactory.Create()) { }

    public SettingsWindow(AppSettings currentSettings, IPlatformServices platform)
    {
        // Deep-copy via JSON round-trip so Cancel reverts cleanly.
        _draft = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(currentSettings)) ?? new AppSettings();
        _platform = platform;
        InitializeComponent();
        WireUp();
        PopulateControls();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public Task<AppSettings?> ShowDialogAsync()
    {
        _resultTcs = new TaskCompletionSource<AppSettings?>();
        // No owner window in tray-only app; ShowDialog requires an owner, so we Show()
        // and resolve the task on close.
        Closed += (_, _) => _resultTcs?.TrySetResult(null);
        Show();
        return _resultTcs.Task;
    }

    private void WireUp()
    {
        // Launch mode label
        this.FindControl<CheckBox>("chkManagerLaunchOnSystemStart")!.Content = _platform.LaunchAtLoginLabel;

        // Launch mode combo
        var cmbLaunchMode = this.FindControl<ComboBox>("cmbLaunchMode")!;
        cmbLaunchMode.ItemsSource = new[]
        {
            "Windows Portable (python_embeded + ComfyUI/main.py)",
            "Venv (ComfyUI clone with ./venv/)",
            "Custom (specify python + main.py paths)"
        };
        cmbLaunchMode.SelectionChanged += (_, _) => UpdateLaunchModeUi();

        // Verbose levels
        var cmbVerbose = this.FindControl<ComboBox>("cmbVerboseLevel")!;
        cmbVerbose.ItemsSource = Enum.GetNames(typeof(ComfyUIFlags.LogLevel));

        // Browse buttons
        this.FindControl<Button>("btnBrowsePath")!.Click   += async (_, _) => await PickFolderInto("txtComfyUIPath", "Select ComfyUI folder");
        this.FindControl<Button>("btnBrowsePython")!.Click += async (_, _) => await PickFileInto("txtCustomPython", "Select python executable", anyFile: true);
        this.FindControl<Button>("btnBrowseMain")!.Click   += async (_, _) => await PickFileInto("txtCustomMain", "Select main.py", pyOnly: true);
        this.FindControl<Button>("btnBrowseOutput")!.Click += async (_, _) => await PickFolderInto("txtOutputDir", "Select default output directory");
        this.FindControl<Button>("btnBrowseModels")!.Click += async (_, _) => await PickFileInto("txtExtraModelsPath", "Select extra_model_paths.yaml", yamlOnly: true);

        // Front-end "latest" checkbox toggles the version textbox
        var chkLatest = this.FindControl<CheckBox>("chkFrontEndVersionLatest")!;
        var txtFe = this.FindControl<TextBox>("txtFrontEndVersion")!;
        chkLatest.IsCheckedChanged += (_, _) => txtFe.IsEnabled = chkLatest.IsChecked != true;

        // OK / Cancel
        this.FindControl<Button>("btnOk")!.Click += (_, _) =>
        {
            SaveControlsToDraft();
            _resultTcs?.TrySetResult(_draft);
            Close();
        };
        this.FindControl<Button>("btnCancel")!.Click += (_, _) =>
        {
            _resultTcs?.TrySetResult(null);
            Close();
        };

        // Version label
        var asm = Assembly.GetExecutingAssembly();
        var fullVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? asm.GetName().Version?.ToString() ?? "N/A";
        var simple = fullVersion.Split('-')[0].Split('+')[0];
        this.FindControl<TextBlock>("lblVersion")!.Text = $"Version: {simple}";
    }

    private void PopulateControls()
    {
        var s = _draft;

        // General — launch mode + paths
        this.FindControl<ComboBox>("cmbLaunchMode")!.SelectedIndex = (int)s.LaunchMode;
        this.FindControl<TextBox>("txtComfyUIPath")!.Text = s.ComfyUIPath;
        this.FindControl<TextBox>("txtCustomPython")!.Text = s.CustomPythonExecutable;
        this.FindControl<TextBox>("txtCustomMain")!.Text = s.CustomMainScript;
        UpdateLaunchModeUi();

        // General — manager toggles
        this.FindControl<CheckBox>("chkManagerAutoStart")!.IsChecked = s.AutoStartServerOnLaunch;
        this.FindControl<CheckBox>("chkManagerAutoRestart")!.IsChecked = s.AutoRestartOnCrash;
        this.FindControl<CheckBox>("chkManagerLaunchOnSystemStart")!.IsChecked = _platform.IsLaunchAtLoginEnabled();

        // General — server
        this.FindControl<NumericUpDown>("numPort")!.Value = s.Flags.Port;
        this.FindControl<CheckBox>("chkDisableAutoLaunch")!.IsChecked = s.Flags.DisableAutoLaunch;
        this.FindControl<CheckBox>("chkDisableMetadata")!.IsChecked = s.Flags.DisableMetadata;
        this.FindControl<CheckBox>("chkMultiUser")!.IsChecked = s.Flags.MultiUser;
        this.FindControl<CheckBox>("chkDontPrintServer")!.IsChecked = s.Flags.DontPrintServer;

        // Performance
        this.FindControl<CheckBox>("chkForceFp16")!.IsChecked = s.Flags.ForceFp16;
        this.FindControl<CheckBox>("chkForceFp32")!.IsChecked = s.Flags.ForceFp32;
        this.FindControl<CheckBox>("chkDisableXformers")!.IsChecked = s.Flags.DisableXformers;
        SelectAttention(s.Flags.Attention);

        // Hardware
        SelectProcessor(s.Flags.Processor);
        SelectVram(s.Flags.VramMode);
        this.FindControl<NumericUpDown>("numCudaDevice")!.Value = s.Flags.CudaDevice;

        // Advanced
        this.FindControl<CheckBox>("chkDisableCustomNodes")!.IsChecked = s.Flags.DisableAllCustomNodes;
        this.FindControl<ComboBox>("cmbVerboseLevel")!.SelectedItem = s.Flags.VerboseLevel.ToString();
        this.FindControl<TextBox>("txtOutputDir")!.Text = s.Flags.OutputDirectory;
        this.FindControl<TextBox>("txtExtraModelsPath")!.Text = s.Flags.ExtraModelPathsConfig;
        this.FindControl<TextBox>("txtFrontEndVersion")!.Text = s.Flags.CustomFrontEndVersion;
        var chkLatest = this.FindControl<CheckBox>("chkFrontEndVersionLatest")!;
        chkLatest.IsChecked = s.Flags.UseLatestFrontEnd;
        this.FindControl<TextBox>("txtFrontEndVersion")!.IsEnabled = chkLatest.IsChecked != true;
    }

    private void SaveControlsToDraft()
    {
        var s = _draft;

        s.LaunchMode = (LaunchMode)this.FindControl<ComboBox>("cmbLaunchMode")!.SelectedIndex;
        s.ComfyUIPath = this.FindControl<TextBox>("txtComfyUIPath")!.Text ?? "";
        s.CustomPythonExecutable = this.FindControl<TextBox>("txtCustomPython")!.Text ?? "";
        s.CustomMainScript = this.FindControl<TextBox>("txtCustomMain")!.Text ?? "";

        s.AutoStartServerOnLaunch = this.FindControl<CheckBox>("chkManagerAutoStart")!.IsChecked == true;
        s.AutoRestartOnCrash = this.FindControl<CheckBox>("chkManagerAutoRestart")!.IsChecked == true;
        var wantLaunchAtLogin = this.FindControl<CheckBox>("chkManagerLaunchOnSystemStart")!.IsChecked == true;
        s.LaunchOnSystemStart = wantLaunchAtLogin;
        try { _platform.SetLaunchAtLogin(wantLaunchAtLogin, Environment.ProcessPath ?? "comfyui-server-manager"); }
        catch (Exception ex) { Console.Error.WriteLine(ex); }

        s.Flags.Port = (int)(this.FindControl<NumericUpDown>("numPort")!.Value ?? 8188m);
        s.Flags.DisableAutoLaunch = this.FindControl<CheckBox>("chkDisableAutoLaunch")!.IsChecked == true;
        s.Flags.DisableMetadata = this.FindControl<CheckBox>("chkDisableMetadata")!.IsChecked == true;
        s.Flags.MultiUser = this.FindControl<CheckBox>("chkMultiUser")!.IsChecked == true;
        s.Flags.DontPrintServer = this.FindControl<CheckBox>("chkDontPrintServer")!.IsChecked == true;

        s.Flags.ForceFp16 = this.FindControl<CheckBox>("chkForceFp16")!.IsChecked == true;
        s.Flags.ForceFp32 = this.FindControl<CheckBox>("chkForceFp32")!.IsChecked == true;
        s.Flags.DisableXformers = this.FindControl<CheckBox>("chkDisableXformers")!.IsChecked == true;
        s.Flags.Attention = ReadAttention();

        s.Flags.Processor = ReadProcessor();
        s.Flags.VramMode = ReadVram();
        s.Flags.CudaDevice = (int)(this.FindControl<NumericUpDown>("numCudaDevice")!.Value ?? 0m);

        s.Flags.DisableAllCustomNodes = this.FindControl<CheckBox>("chkDisableCustomNodes")!.IsChecked == true;
        var verbose = this.FindControl<ComboBox>("cmbVerboseLevel")!.SelectedItem as string;
        s.Flags.VerboseLevel = !string.IsNullOrEmpty(verbose)
            ? Enum.Parse<ComfyUIFlags.LogLevel>(verbose)
            : ComfyUIFlags.LogLevel.NONE;
        s.Flags.OutputDirectory = this.FindControl<TextBox>("txtOutputDir")!.Text ?? "";
        s.Flags.ExtraModelPathsConfig = this.FindControl<TextBox>("txtExtraModelsPath")!.Text ?? "";
        s.Flags.CustomFrontEndVersion = this.FindControl<TextBox>("txtFrontEndVersion")!.Text ?? "";
        s.Flags.UseLatestFrontEnd = this.FindControl<CheckBox>("chkFrontEndVersionLatest")!.IsChecked == true;
    }

    private void UpdateLaunchModeUi()
    {
        var idx = this.FindControl<ComboBox>("cmbLaunchMode")!.SelectedIndex;
        var mode = (LaunchMode)Math.Max(0, idx);
        var customPanel = this.FindControl<Grid>("customPathsPanel")!;
        var pathBox = this.FindControl<TextBox>("txtComfyUIPath")!;
        var hint = this.FindControl<TextBlock>("lblLaunchHint")!;
        switch (mode)
        {
            case LaunchMode.WindowsPortable:
                pathBox.IsEnabled = true;
                customPanel.IsVisible = false;
                hint.Text = "Path should be the folder that contains both python_embeded/ and ComfyUI/. Windows portable layout.";
                break;
            case LaunchMode.Venv:
                pathBox.IsEnabled = true;
                customPanel.IsVisible = false;
                hint.Text =
                    "Path should be a folder that has a venv/ subfolder AND main.py reachable from it.\n"
                    + "Either of these layouts works — just pick the outer folder:\n"
                    + "  <picked>/main.py             + <picked>/venv/    (venv inside the clone)\n"
                    + "  <picked>/ComfyUI/main.py    + <picked>/venv/    (venv next to ComfyUI/)\n"
                    + "If your venv is elsewhere, switch to Custom mode and point at the python + main.py explicitly.";
                break;
            case LaunchMode.Custom:
                pathBox.IsEnabled = false;
                customPanel.IsVisible = true;
                hint.Text = "Specify the python executable and main.py path explicitly. Working directory will be set to the folder containing main.py.";
                break;
        }
    }

    private void SelectAttention(ComfyUIFlags.AttentionType a)
    {
        var map = new (string name, ComfyUIFlags.AttentionType v)[]
        {
            ("rbAttnPytorch", ComfyUIFlags.AttentionType.pytorch),
            ("rbAttnSplit",   ComfyUIFlags.AttentionType.split),
            ("rbAttnQuad",    ComfyUIFlags.AttentionType.quad),
            ("rbAttnSage",    ComfyUIFlags.AttentionType.sage),
            ("rbAttnFlash",   ComfyUIFlags.AttentionType.flash),
        };
        foreach (var (n, v) in map) this.FindControl<RadioButton>(n)!.IsChecked = (v == a);
    }

    private ComfyUIFlags.AttentionType ReadAttention()
    {
        if (this.FindControl<RadioButton>("rbAttnSplit")!.IsChecked == true) return ComfyUIFlags.AttentionType.split;
        if (this.FindControl<RadioButton>("rbAttnQuad")!.IsChecked == true) return ComfyUIFlags.AttentionType.quad;
        if (this.FindControl<RadioButton>("rbAttnSage")!.IsChecked == true) return ComfyUIFlags.AttentionType.sage;
        if (this.FindControl<RadioButton>("rbAttnFlash")!.IsChecked == true) return ComfyUIFlags.AttentionType.flash;
        return ComfyUIFlags.AttentionType.pytorch;
    }

    private void SelectProcessor(ComfyUIFlags.ProcessingUnit p)
    {
        this.FindControl<RadioButton>("rbProcDefault")!.IsChecked = p == ComfyUIFlags.ProcessingUnit.default_gpu;
        this.FindControl<RadioButton>("rbProcGpuOnly")!.IsChecked = p == ComfyUIFlags.ProcessingUnit.gpu_only;
        this.FindControl<RadioButton>("rbProcCpu")!.IsChecked     = p == ComfyUIFlags.ProcessingUnit.cpu;
    }

    private ComfyUIFlags.ProcessingUnit ReadProcessor()
    {
        if (this.FindControl<RadioButton>("rbProcGpuOnly")!.IsChecked == true) return ComfyUIFlags.ProcessingUnit.gpu_only;
        if (this.FindControl<RadioButton>("rbProcCpu")!.IsChecked == true) return ComfyUIFlags.ProcessingUnit.cpu;
        return ComfyUIFlags.ProcessingUnit.default_gpu;
    }

    private void SelectVram(ComfyUIFlags.VramPreset v)
    {
        this.FindControl<RadioButton>("rbVramNormal")!.IsChecked = v == ComfyUIFlags.VramPreset.normalvram;
        this.FindControl<RadioButton>("rbVramHigh")!.IsChecked   = v == ComfyUIFlags.VramPreset.highvram;
        this.FindControl<RadioButton>("rbVramLow")!.IsChecked    = v == ComfyUIFlags.VramPreset.lowvram;
        this.FindControl<RadioButton>("rbVramNo")!.IsChecked     = v == ComfyUIFlags.VramPreset.novram;
    }

    private ComfyUIFlags.VramPreset ReadVram()
    {
        if (this.FindControl<RadioButton>("rbVramHigh")!.IsChecked == true) return ComfyUIFlags.VramPreset.highvram;
        if (this.FindControl<RadioButton>("rbVramLow")!.IsChecked == true) return ComfyUIFlags.VramPreset.lowvram;
        if (this.FindControl<RadioButton>("rbVramNo")!.IsChecked == true) return ComfyUIFlags.VramPreset.novram;
        return ComfyUIFlags.VramPreset.normalvram;
    }

    private async Task PickFolderInto(string textBoxName, string title)
    {
        var box = this.FindControl<TextBox>(textBoxName)!;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(box.Text ?? "")
        });
        if (folders.Count > 0) box.Text = folders[0].Path.LocalPath;
    }

    private async Task PickFileInto(string textBoxName, string title, bool anyFile = false, bool pyOnly = false, bool yamlOnly = false)
    {
        var box = this.FindControl<TextBox>(textBoxName)!;
        var filters = new System.Collections.Generic.List<FilePickerFileType>();
        if (pyOnly) filters.Add(new FilePickerFileType("Python script") { Patterns = new[] { "*.py" } });
        if (yamlOnly) filters.Add(new FilePickerFileType("YAML") { Patterns = new[] { "*.yaml", "*.yml" } });
        if (anyFile || filters.Count == 0) filters.Add(FilePickerFileTypes.All);

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters
        });
        if (files.Count > 0) box.Text = files[0].Path.LocalPath;
    }
}
