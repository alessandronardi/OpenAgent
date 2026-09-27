using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using OpenAgent.Agent.Security;
using OpenAgent.App.Native;
using OpenAgent.Data;
using OpenAgent.Data.Hardware;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using ComboBoxItem = System.Windows.Controls.ComboBoxItem;
using MessageBox = System.Windows.MessageBox;
using RadioButton = System.Windows.Controls.RadioButton;
using TextBox = System.Windows.Controls.TextBox;
using OpenAgent.Model;

namespace OpenAgent.App;

public sealed class AllowedPathViewModel
{
    public string Path { get; set; } = string.Empty;
    public bool IsCustom { get; set; }
    public string BadgeText => IsCustom ? "Personalizzata" : "Predefinita";
    public Brush BadgeBackground => IsCustom
        ? new SolidColorBrush(Color.FromRgb(46, 27, 78))
        : new SolidColorBrush(Color.FromRgb(22, 45, 34));
    public Brush BadgeForeground => IsCustom
        ? new SolidColorBrush(Color.FromRgb(216, 180, 254))
        : new SolidColorBrush(Color.FromRgb(110, 231, 183));
}

public sealed class MemoryItemViewModel
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string FormattedDate { get; set; } = string.Empty;
}

public partial class SettingsWindow : Window
{
    private readonly IUserSettingsRepository _settingsRepository;
    private readonly PermissionManager _permissionManager;
    private readonly Action<AppSettings>? _onSettingsApplied;
    private AppSettings _currentSettings;
    private bool _isUpdatingApiKey;
    private bool _isKeyVisible;

    private readonly ObservableCollection<AllowedPathViewModel> _allowedPaths = [];
    private readonly ObservableCollection<MemoryItemViewModel> _userMemories = [];

    public SettingsWindow(
        IUserSettingsRepository settingsRepository,
        PermissionManager permissionManager,
        AppSettings currentSettings,
        Action<AppSettings>? onSettingsApplied = null)
    {
        InitializeComponent();

        _settingsRepository = settingsRepository ?? throw new ArgumentNullException(nameof(settingsRepository));
        _permissionManager = permissionManager ?? throw new ArgumentNullException(nameof(permissionManager));
        _currentSettings = currentSettings?.Clone() ?? new AppSettings();
        _onSettingsApplied = onSettingsApplied;

        ListAllowedPaths.ItemsSource = _allowedPaths;
        ListUserMemory.ItemsSource = _userMemories;

        Loaded += async (_, _) =>
        {
            LoadSettingsToUi();
            RefreshAllowedPathsUi();
            await RefreshUserMemoriesUiAsync();
        };
    }

    private void LoadSettingsToUi()
    {
        // 1. Model Profile
        if (_currentSettings.ModelProfile == AppSettings.ProfileCoder15B)
        {
            SelectComboBoxByTag(ComboModelProfile, "Coder15B");
        }
        else if (_currentSettings.ModelProfile == AppSettings.ProfileQwen3B)
        {
            SelectComboBoxByTag(ComboModelProfile, "Qwen3B");
        }
        else if (_currentSettings.ModelProfile == AppSettings.ProfileGemma2B)
        {
            SelectComboBoxByTag(ComboModelProfile, "Gemma2B");
        }
        else if (_currentSettings.ModelProfile == AppSettings.DefaultProfile17B)
        {
            SelectComboBoxByTag(ComboModelProfile, "1.7B");
        }
        else if (_currentSettings.ModelProfile == AppSettings.ProfileCustom)
        {
            SelectComboBoxByTag(ComboModelProfile, "Custom");
        }
        else
        {
            SelectComboBoxByTag(ComboModelProfile, "Coder15B");
        }

        TxtModelPath.Text = _currentSettings.ModelPath;

        // 2. CPU Threads
        SelectComboBoxByPrefix(ComboCpuThreads, $"{_currentSettings.CpuThreads} thread");

        // 3. Context Size
        SelectComboBoxByPrefix(ComboContextSize, $"{_currentSettings.ContextSize} token");

        // 4. Hardware Acceleration Device
        if (string.Equals(_currentSettings.Device, "Vulkan0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_currentSettings.Device, "vulkan", StringComparison.OrdinalIgnoreCase))
        {
            ComboDevice.SelectedIndex = 1;
        }
        else
        {
            ComboDevice.SelectedIndex = 0;
        }

        // 5. Execution Timeout
        SelectComboBoxByTag(ComboExecutionTimeout, _currentSettings.ExecutionTimeoutSeconds.ToString());

        // 6. Server Port and Host
        TxtServerPort.Text = _currentSettings.Port.ToString();
        TxtServerHost.Text = _currentSettings.Host;

        // 7. Windows Integration
        ChkStartWithWindows.IsChecked = WindowsAutostartManager.IsAutostartEnabled();
        ChkStartMinimized.IsChecked = _currentSettings.StartMinimizedToTray;

        // 8. Model Info Card
        UpdateModelInfoCard(_currentSettings.ModelProfile, _currentSettings.ModelPath);

        // 9. Hardware Auto-Profiling Card
        DisplayDetectedHardwareInfo();
        ChkAutoTuneOnStartup.IsChecked = _currentSettings.AutoTuneOnStartup;

        // 10. Cloud Provider (Google Gemini)
        _isUpdatingApiKey = true;
        try
        {
            PwdGeminiApiKey.Password = _currentSettings.GeminiApiKey;
            TxtGeminiApiKeyVisible.Text = _currentSettings.GeminiApiKey;
        }
        finally
        {
            _isUpdatingApiKey = false;
        }

        _isKeyVisible = false;
        PwdGeminiApiKey.Visibility = Visibility.Visible;
        TxtGeminiApiKeyVisible.Visibility = Visibility.Collapsed;
        BtnToggleKeyVisibility.Content = "👁️";

        UpdateApiKeyPlaceholder();
        UpdateCloudStatusBadge(_currentSettings.GeminiApiKey);

        if (!string.IsNullOrWhiteSpace(_currentSettings.CloudModelName))
        {
            SelectComboBoxByTag(ComboCloudModel, _currentSettings.CloudModelName);
        }
        ChkAutoCloudComplexity.IsChecked = _currentSettings.AutoCloudOnComplexity;
        ChkEnableCloudEscalation.IsChecked = _currentSettings.EnableCloudEscalation;
    }

    private void DisplayDetectedHardwareInfo(HardwareSpecs? existingSpecs = null)
    {
        try
        {
            string rootDir = MainWindow.FindProjectRootDir(AppContext.BaseDirectory);
            var specs = existingSpecs ?? HardwareDetector.DetectSpecs(rootDir);

            TxtHwCpu.Text = $"CPU: {specs.CpuName} ({specs.PhysicalCores} core / {specs.LogicalCores} thr)";
            TxtHwRam.Text = $"RAM: {specs.AvailableRamGb:F1} GB liberi su {specs.TotalRamGb:F1} GB";
            TxtHwGpu.Text = $"GPU: {specs.GpuName ?? (specs.HasVulkanSupport ? "GPU compatibile Vulkan" : "Nessuna GPU dedicata")}";

            TxtHardwareTier.Text = specs.Tier switch
            {
                HardwareTier.Entry => "Tier Risparmio",
                HardwareTier.Balanced => "Tier Bilanciato",
                HardwareTier.HighPerformance => "Tier Alte Prestazioni",
                _ => "Tier Rilevato"
            };

            if (BadgeHardwareTier != null)
            {
                BadgeHardwareTier.Background = specs.Tier switch
                {
                    HardwareTier.HighPerformance => new SolidColorBrush(Color.FromRgb(16, 56, 32)),
                    HardwareTier.Balanced => new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    _ => new SolidColorBrush(Color.FromRgb(55, 48, 20))
                };
            }

            TxtHwProfileStatus.Text = $"✓ Attuale: {_currentSettings.CpuThreads} thread · {_currentSettings.ContextSize} ctx · {_currentSettings.Device}";
        }
        catch
        {
            TxtHwCpu.Text = "CPU: Rilevamento non disponibile";
            TxtHwRam.Text = "RAM: Rilevamento non disponibile";
            TxtHwGpu.Text = "GPU: Rilevamento non disponibile";
        }
    }

    private void OnAutoTuneHardwareClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string rootDir = MainWindow.FindProjectRootDir(AppContext.BaseDirectory);
            var specs = HardwareDetector.DetectSpecs(rootDir);
            var result = HardwareProfiler.OptimizeSettings(_currentSettings, specs, rootDir);

            _currentSettings = result.OptimizedSettings;
            LoadSettingsToUi();
            DisplayDetectedHardwareInfo(specs);

            TxtHwProfileStatus.Text = $"⚡ Ottimizzato per {specs.PhysicalCores} core ({specs.Tier})";
            TxtHwProfileStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));

            string details = string.Join("\n", result.AppliedOptimizations.Select(o => "• " + o));
            MessageBox.Show(
                $"Profilazione hardware completata con successo!\n\nSpecifiche Rilevate:\n{specs.Summary}\n\nOttimizzazioni Applicate:\n{details}",
                "Ottimizzazione Hardware OpenAgent",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante la profilazione hardware: {ex.Message}", "OpenAgent", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void SelectComboBoxByPrefix(ComboBox combo, string prefix)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Content?.ToString()?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static void SelectComboBoxByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private void RefreshAllowedPathsUi()
    {
        _allowedPaths.Clear();

        var customSet = new HashSet<string>(_currentSettings.CustomAllowedDirectories, StringComparer.OrdinalIgnoreCase);

        foreach (var p in _permissionManager.AllowedPaths)
        {
            bool isCustom = customSet.Contains(p) || _permissionManager.CustomAllowedPaths.Contains(p);
            _allowedPaths.Add(new AllowedPathViewModel
            {
                Path = p,
                IsCustom = isCustom
            });
        }
    }

    private async Task RefreshUserMemoriesUiAsync()
    {
        _userMemories.Clear();
        try
        {
            var memories = await _settingsRepository.GetAllMemoriesAsync();
            foreach (var m in memories)
            {
                _userMemories.Add(new MemoryItemViewModel
                {
                    Key = m.Key,
                    Value = m.Value,
                    FormattedDate = m.UpdatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm")
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento memoria: {ex.Message}", "OpenAgent", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnTabSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (PanelGeneral == null || PanelAllowlist == null || PanelMemory == null || PanelCloud == null) return;

        if (sender is RadioButton clickedRadio && clickedRadio.IsChecked != true)
        {
            clickedRadio.IsChecked = true;
        }

        PanelGeneral.Visibility = TabBtnGeneral.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelAllowlist.Visibility = TabBtnAllowlist.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelMemory.Visibility = TabBtnMemory.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PanelCloud.Visibility = TabBtnCloud.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnModelProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboModelProfile.SelectedItem is ComboBoxItem item && TxtModelPath != null)
        {
            string? tag = item.Tag?.ToString();
            string profile = GetSelectedProfileName();

            if (tag == "Coder15B")
            {
                TxtModelPath.Text = AppSettings.DefaultModelPathCoder15B;
                TxtModelPath.IsEnabled = false;
            }
            else if (tag == "Qwen3B")
            {
                TxtModelPath.Text = AppSettings.DefaultModelPathQwen3B;
                TxtModelPath.IsEnabled = false;
            }
            else if (tag == "Gemma2B")
            {
                TxtModelPath.Text = AppSettings.DefaultModelPathGemma2B;
                TxtModelPath.IsEnabled = false;
            }
            else if (tag == "1.7B")
            {
                TxtModelPath.Text = AppSettings.DefaultModelPath17B;
                TxtModelPath.IsEnabled = false;
            }
            else
            {
                TxtModelPath.IsEnabled = true;
            }

            UpdateModelInfoCard(profile, TxtModelPath.Text);

            if (ChkAutoApplyPreset?.IsChecked == true)
            {
                var preset = AppSettings.GetModelPreset(profile);
                ApplyPreset(preset);
            }
        }
    }

    private void OnApplyRecommendedPresetClicked(object sender, RoutedEventArgs e)
    {
        string profile = GetSelectedProfileName();
        var preset = AppSettings.GetModelPreset(profile);
        ApplyPreset(preset);
    }

    private string GetSelectedProfileName()
    {
        if (ComboModelProfile.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            return item.Tag.ToString() switch
            {
                "Coder15B" => AppSettings.ProfileCoder15B,
                "Qwen3B" => AppSettings.ProfileQwen3B,
                "Gemma2B" => AppSettings.ProfileGemma2B,
                "1.7B" => AppSettings.DefaultProfile17B,
                "Custom" => AppSettings.ProfileCustom,
                _ => AppSettings.DefaultProfile17B
            };
        }

        return AppSettings.DefaultProfile17B;
    }

    private void ApplyPreset(ModelPreset preset)
    {
        SelectComboBoxByPrefix(ComboCpuThreads, $"{preset.CpuThreads} thread");
        SelectComboBoxByPrefix(ComboContextSize, $"{preset.ContextSize} token");
        SelectComboBoxByTag(ComboExecutionTimeout, preset.ExecutionTimeoutSeconds.ToString());
    }

    private void UpdateModelInfoCard(string? profileName, string? modelPath)
    {
        if (CardModelInfo == null || TxtModelStatus == null || TxtModelFileSize == null ||
            BadgeModelStatus == null || TxtModelSpecialization == null || TxtModelRecommendedParams == null)
        {
            return;
        }

        string effectivePath = modelPath ?? string.Empty;
        string rootDir = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(rootDir);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenAgent.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, "models")))
            {
                rootDir = dir.FullName;
                break;
            }
            dir = dir.Parent;
        }

        string absPath = Path.IsPathRooted(effectivePath) ? effectivePath : Path.Combine(rootDir, effectivePath);

        if (File.Exists(absPath))
        {
            try
            {
                var fi = new FileInfo(absPath);
                double gb = fi.Length / (1024.0 * 1024.0 * 1024.0);
                TxtModelStatus.Text = "✓ Scaricato e pronto";
                TxtModelFileSize.Text = $" · {gb:F2} GB";
                BadgeModelStatus.Background = new SolidColorBrush(Color.FromRgb(6, 78, 59));
                BadgeModelStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(5, 150, 105));
                TxtModelStatus.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            }
            catch
            {
                TxtModelStatus.Text = "✓ Presente su disco";
                TxtModelFileSize.Text = string.Empty;
            }
        }
        else
        {
            TxtModelStatus.Text = "⚠️ Non presente sul disco";
            TxtModelFileSize.Text = string.Empty;
            BadgeModelStatus.Background = new SolidColorBrush(Color.FromRgb(69, 26, 3));
            BadgeModelStatus.BorderBrush = new SolidColorBrush(Color.FromRgb(217, 119, 6));
            TxtModelStatus.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        }

        var preset = AppSettings.GetModelPreset(profileName);
        TxtModelSpecialization.Text = preset.Specialization;
        TxtModelRecommendedParams.Text = $"Stima: {preset.RamFootprintEstimate} · Velocità: {preset.SpeedEstimate} · Suggeriti: {preset.CpuThreads} thread CPU, {preset.ContextSize} token ctx, timeout {preset.ExecutionTimeoutSeconds}s";
    }

    private void OnBrowseModelPathClicked(object sender, RoutedEventArgs e)
    {
        var openDlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Seleziona file modello GGUF",
            Filter = "Modelli GGUF (*.gguf)|*.gguf|Tutti i file (*.*)|*.*",
            CheckFileExists = true
        };

        if (openDlg.ShowDialog() == true)
        {
            TxtModelPath.Text = openDlg.FileName;
            ComboModelProfile.SelectedIndex = 4; // Custom
            UpdateModelInfoCard(AppSettings.ProfileCustom, TxtModelPath.Text);
        }
    }

    private void OnAddAllowedFolderClicked(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Seleziona una cartella da autorizzare per OpenAgent",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            string folder = dlg.SelectedPath;
            try
            {
                _permissionManager.AddAllowedDirectory(folder);
                if (!_currentSettings.CustomAllowedDirectories.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    _currentSettings.CustomAllowedDirectories.Add(folder);
                }
                RefreshAllowedPathsUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Autorizzazione Cartella", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnRemoveAllowedFolderClicked(object sender, RoutedEventArgs e)
    {
        if (ListAllowedPaths.SelectedItem is AllowedPathViewModel item)
        {
            _permissionManager.RemoveAllowedDirectory(item.Path);
            _currentSettings.CustomAllowedDirectories.RemoveAll(x => x.Equals(item.Path, StringComparison.OrdinalIgnoreCase));
            RefreshAllowedPathsUi();
        }
    }

    private void OnResetDefaultAllowedFoldersClicked(object sender, RoutedEventArgs e)
    {
        _permissionManager.ResetDefaultPolicies();
        _currentSettings.CustomAllowedDirectories.Clear();
        RefreshAllowedPathsUi();
    }

    private void OnMemoryItemSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ListUserMemory.SelectedItem is MemoryItemViewModel item)
        {
            TxtMemoryKey.Text = item.Key;
            TxtMemoryValue.Text = item.Value;
        }
    }

    private async void OnSaveMemoryClicked(object sender, RoutedEventArgs e)
    {
        string key = TxtMemoryKey.Text.Trim();
        string value = TxtMemoryValue.Text.Trim();

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show("Inserisci sia la chiave che il valore per memorizzare la nota.", "Memoria Agente", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            await _settingsRepository.SetMemoryAsync(key, value);
            TxtMemoryKey.Clear();
            TxtMemoryValue.Clear();
            await RefreshUserMemoriesUiAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore salvataggio memoria: {ex.Message}", "Memoria Agente", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnDeleteMemoryClicked(object sender, RoutedEventArgs e)
    {
        if (ListUserMemory.SelectedItem is MemoryItemViewModel item)
        {
            try
            {
                await _settingsRepository.DeleteMemoryAsync(item.Key);
                TxtMemoryKey.Clear();
                TxtMemoryValue.Clear();
                await RefreshUserMemoriesUiAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore eliminazione: {ex.Message}", "Memoria Agente", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnClearAllMemoriesClicked(object sender, RoutedEventArgs e)
    {
        var res = MessageBox.Show(
            "Sei sicuro di voler eliminare tutte le preferenze e note memorizzate dall'agente?",
            "Conferma Svuotamento Memoria",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (res == MessageBoxResult.Yes)
        {
            try
            {
                await _settingsRepository.ClearMemoriesAsync();
                TxtMemoryKey.Clear();
                TxtMemoryValue.Clear();
                await RefreshUserMemoriesUiAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore svuotamento memoria: {ex.Message}", "Memoria Agente", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private async void OnSaveAndApplyClicked(object sender, RoutedEventArgs e)
    {
        // 1. Model Profile
        string profile = GetSelectedProfileName();

        _currentSettings.ModelProfile = profile;
        _currentSettings.ModelPath = TxtModelPath.Text.Trim();

        // 2. CPU Threads
        if (ComboCpuThreads.SelectedItem is ComboBoxItem threadItem)
        {
            string txt = threadItem.Content?.ToString() ?? "6";
            string numStr = new string(txt.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(numStr, out int t)) _currentSettings.CpuThreads = t;
        }

        // 3. Context Size
        if (ComboContextSize.SelectedItem is ComboBoxItem ctxItem)
        {
            string txt = ctxItem.Content?.ToString() ?? "4096";
            string numStr = new string(txt.TakeWhile(char.IsDigit).ToArray());
            if (int.TryParse(numStr, out int c)) _currentSettings.ContextSize = c;
        }

        // 4. Hardware Acceleration Device
        if (ComboDevice.SelectedItem is ComboBoxItem devItem && devItem.Tag != null)
        {
            _currentSettings.Device = devItem.Tag.ToString() ?? "none";
        }

        // 5. Per-Step Timeout
        if (ComboExecutionTimeout.SelectedItem is ComboBoxItem timeoutItem &&
            timeoutItem.Tag != null &&
            int.TryParse(timeoutItem.Tag.ToString(), out int timeoutSec))
        {
            _currentSettings.ExecutionTimeoutSeconds = timeoutSec;
        }

        // 6. Server Port and Host
        if (int.TryParse(TxtServerPort.Text.Trim(), out int port) && port > 0)
        {
            _currentSettings.Port = port;
        }
        _currentSettings.Host = TxtServerHost.Text.Trim();

        // 5. Windows Autostart & Minimize
        bool autostart = ChkStartWithWindows.IsChecked == true;
        bool minimized = ChkStartMinimized.IsChecked == true;
        _currentSettings.StartWithWindows = autostart;
        _currentSettings.StartMinimizedToTray = minimized;
        _currentSettings.AutoTuneOnStartup = ChkAutoTuneOnStartup.IsChecked == true;

        WindowsAutostartManager.SetAutostart(autostart, minimized);

        // 6. Cloud Settings (Google Gemini)
        _currentSettings.GeminiApiKey = GetEffectiveGeminiApiKey();
        if (ComboCloudModel.SelectedItem is ComboBoxItem cloudItem && cloudItem.Tag != null)
        {
            _currentSettings.CloudModelName = cloudItem.Tag.ToString() ?? "gemini-3.8-flash";
        }
        _currentSettings.AutoCloudOnComplexity = ChkAutoCloudComplexity.IsChecked == true;
        _currentSettings.EnableCloudEscalation = ChkEnableCloudEscalation.IsChecked == true;

        // 7. Save to SQLite
        try
        {
            await _settingsRepository.SaveSettingsAsync(_currentSettings);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore salvataggio impostazioni su database: {ex.Message}", "OpenAgent", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // 7. Apply to PermissionManager
        _permissionManager.SetCustomAllowedPaths(_currentSettings.CustomAllowedDirectories);

        // 8. Invoke callback
        _onSettingsApplied?.Invoke(_currentSettings);

        Close();
    }

    private string GetEffectiveGeminiApiKey()
    {
        return (_isKeyVisible ? TxtGeminiApiKeyVisible.Text : PwdGeminiApiKey.Password)?.Trim() ?? string.Empty;
    }

    private void UpdateApiKeyPlaceholder()
    {
        if (TxtApiKeyPlaceholder == null) return;
        string key = GetEffectiveGeminiApiKey();
        TxtApiKeyPlaceholder.Visibility = string.IsNullOrEmpty(key) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateCloudStatusBadge(string? key)
    {
        if (BadgeCloudStatus == null || TxtCloudStatusBadge == null) return;

        bool hasKey = !string.IsNullOrWhiteSpace(key);
        if (hasKey)
        {
            BadgeCloudStatus.Background = new SolidColorBrush(Color.FromRgb(16, 56, 32));
            TxtCloudStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            TxtCloudStatusBadge.Text = "Chiave Attiva (Gemini Cloud)";
        }
        else
        {
            BadgeCloudStatus.Background = new SolidColorBrush(Color.FromRgb(55, 47, 20));
            TxtCloudStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            TxtCloudStatusBadge.Text = "Chiave Non Configurata";
        }
    }

    private void OnGeminiApiKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingApiKey) return;
        _isUpdatingApiKey = true;
        try
        {
            TxtGeminiApiKeyVisible.Text = PwdGeminiApiKey.Password;
        }
        finally
        {
            _isUpdatingApiKey = false;
        }

        UpdateApiKeyPlaceholder();
        UpdateCloudStatusBadge(PwdGeminiApiKey.Password);
    }

    private void OnGeminiApiKeyTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingApiKey) return;
        _isUpdatingApiKey = true;
        try
        {
            PwdGeminiApiKey.Password = TxtGeminiApiKeyVisible.Text;
        }
        finally
        {
            _isUpdatingApiKey = false;
        }

        UpdateApiKeyPlaceholder();
        UpdateCloudStatusBadge(TxtGeminiApiKeyVisible.Text);
    }

    private void OnToggleKeyVisibilityClicked(object sender, RoutedEventArgs e)
    {
        _isKeyVisible = !_isKeyVisible;
        if (_isKeyVisible)
        {
            TxtGeminiApiKeyVisible.Text = PwdGeminiApiKey.Password;
            TxtGeminiApiKeyVisible.Visibility = Visibility.Visible;
            PwdGeminiApiKey.Visibility = Visibility.Collapsed;
            BtnToggleKeyVisibility.Content = "🔒";
            TxtGeminiApiKeyVisible.Focus();
            TxtGeminiApiKeyVisible.CaretIndex = TxtGeminiApiKeyVisible.Text.Length;
        }
        else
        {
            PwdGeminiApiKey.Password = TxtGeminiApiKeyVisible.Text;
            PwdGeminiApiKey.Visibility = Visibility.Visible;
            TxtGeminiApiKeyVisible.Visibility = Visibility.Collapsed;
            BtnToggleKeyVisibility.Content = "👁️";
            PwdGeminiApiKey.Focus();
        }
        UpdateApiKeyPlaceholder();
    }

    private void OnPasteApiKeyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                string text = System.Windows.Clipboard.GetText().Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    _isUpdatingApiKey = true;
                    try
                    {
                        PwdGeminiApiKey.Password = text;
                        TxtGeminiApiKeyVisible.Text = text;
                    }
                    finally
                    {
                        _isUpdatingApiKey = false;
                    }

                    UpdateApiKeyPlaceholder();
                    UpdateCloudStatusBadge(text);
                    TxtCloudTestResult.Text = "Chiave incollata dagli appunti. Clicca su 'Verifica Chiave' per testarla.";
                    TxtCloudTestResult.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile leggere dagli appunti: {ex.Message}", "OpenAgent", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnClearApiKeyClicked(object sender, RoutedEventArgs e)
    {
        _isUpdatingApiKey = true;
        try
        {
            PwdGeminiApiKey.Password = string.Empty;
            TxtGeminiApiKeyVisible.Text = string.Empty;
        }
        finally
        {
            _isUpdatingApiKey = false;
        }

        UpdateApiKeyPlaceholder();
        UpdateCloudStatusBadge(string.Empty);
        TxtCloudTestResult.Text = "Chiave rimossa.";
        TxtCloudTestResult.Foreground = new SolidColorBrush(Color.FromRgb(156, 163, 175));
    }

    private async void OnTestCloudConnectionClicked(object sender, RoutedEventArgs e)
    {
        string key = GetEffectiveGeminiApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            TxtCloudTestResult.Text = "⚠️ Inserisci o incolla prima una chiave API valida.";
            TxtCloudTestResult.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            return;
        }

        string model = "gemini-3.8-flash";
        if (ComboCloudModel.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            model = item.Tag.ToString() ?? "gemini-3.8-flash";
        }

        BtnTestCloudConnection.IsEnabled = false;
        TxtCloudTestResult.Text = $"⏳ Verifica connessione a Google Gemini ({model})...";
        TxtCloudTestResult.Foreground = new SolidColorBrush(Color.FromRgb(96, 165, 250));

        try
        {
            var (success, message) = await GeminiClient.ValidateApiKeyAsync(key, model);
            if (success)
            {
                TxtCloudTestResult.Text = $"✅ {message}";
                TxtCloudTestResult.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
                UpdateCloudStatusBadge(key);
            }
            else
            {
                TxtCloudTestResult.Text = $"❌ {message}";
                TxtCloudTestResult.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
            }
        }
        catch (Exception ex)
        {
            TxtCloudTestResult.Text = $"❌ Errore: {ex.Message}";
            TxtCloudTestResult.Foreground = new SolidColorBrush(Color.FromRgb(248, 113, 113));
        }
        finally
        {
            BtnTestCloudConnection.IsEnabled = true;
        }
    }

    private void OnOpenAiStudioLinkClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://aistudio.google.com/app/apikey",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossibile aprire il browser: {ex.Message}\n\nVisita manualmente: https://aistudio.google.com/app/apikey", "Google AI Studio", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnTitleBarMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
