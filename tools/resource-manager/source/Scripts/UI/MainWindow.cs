#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using AOResourceConverter.Converters;
using AOResourceConverter.UI;

namespace AOResourceConverter;

public partial class MainWindow : Control
{
    // Shared state
    private AoVersion _selectedVersion = AoVersion.V133;

    // UI elements
    private TabContainer? _tabs;
    private OptionButton? _versionSelect;
    private Label? _statusLabel;

    // Graphics tab
    private LineEdit? _gfxInputPath;
    private LineEdit? _gfxOutputPath;
    private ProgressBar? _gfxProgress;
    private Label? _gfxStatus;
    private CheckBox? _gfxIsArchive;

    // INITs tab
    private LineEdit? _initInputPath;
    private LineEdit? _initOutputPath;
    private ProgressBar? _initProgress;
    private Label? _initStatus;
    private Label? _initValidation;

    // Maps tab
    private LineEdit? _mapInputPath;
    private LineEdit? _mapOutputPath;
    private ProgressBar? _mapProgress;
    private Label? _mapStatus;

    // File dialogs
    private FileDialog? _dirDialog;
    private FileDialog? _fileDialog;
    private Action<string>? _pendingDirCallback;
    private Action<string>? _pendingFileCallback;

    public override void _Ready()
    {
        BuildUI();
    }

    private void BuildUI()
    {
        // Main vertical layout
        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 0);
        AddChild(root);

        // Header bar
        var header = new PanelContainer();
        var headerStyle = new StyleBoxFlat { BgColor = AppTheme.BG_PANEL };
        headerStyle.ContentMarginLeft = headerStyle.ContentMarginRight = 16;
        headerStyle.ContentMarginTop = headerStyle.ContentMarginBottom = 10;
        header.AddThemeStyleboxOverride("panel", headerStyle);

        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 16);

        var titleLabel = AppTheme.MakeLabel("AO Resource Converter", AppTheme.TEXT_PRIMARY, AppTheme.FONT_XL);
        headerRow.AddChild(titleLabel);

        headerRow.AddChild(AppTheme.MakeLabel("Version:", AppTheme.TEXT_SECONDARY, AppTheme.FONT_MD));

        _versionSelect = AppTheme.MakeOptionButton();
        _versionSelect.AddItem("0.99z", (int)AoVersion.V099z);
        _versionSelect.AddItem("0.11.5", (int)AoVersion.V115);
        _versionSelect.AddItem("0.12.3", (int)AoVersion.V123);
        _versionSelect.AddItem("0.13.3", (int)AoVersion.V133);
        _versionSelect.Selected = 3; // Default to 0.13.3
        _versionSelect.ItemSelected += OnVersionChanged;
        headerRow.AddChild(_versionSelect);

        // Spacer
        var spacer = new Control();
        spacer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headerRow.AddChild(spacer);

        _statusLabel = AppTheme.MakeLabel("Listo", AppTheme.TEXT_MUTED, AppTheme.FONT_SM);
        headerRow.AddChild(_statusLabel);

        header.AddChild(headerRow);
        root.AddChild(header);

        // Tab container
        _tabs = new TabContainer();
        _tabs.SizeFlagsVertical = SizeFlags.ExpandFill;
        _tabs.AddThemeFontSizeOverride("font_size", AppTheme.FONT_MD);
        root.AddChild(_tabs);

        // Build tabs
        _tabs.AddChild(BuildGraphicsTab());
        _tabs.AddChild(BuildInitsTab());
        _tabs.AddChild(BuildMapsTab());

        // Set tab names
        _tabs.SetTabTitle(0, "Graficos");
        _tabs.SetTabTitle(1, "INITs");
        _tabs.SetTabTitle(2, "Mapas");

        // File dialogs
        _dirDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenDir,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Seleccionar carpeta",
            Size = new Vector2I(600, 400),
        };
        _dirDialog.DirSelected += path => _pendingDirCallback?.Invoke(path);
        AddChild(_dirDialog);

        _fileDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Title = "Seleccionar archivo",
            Size = new Vector2I(600, 400),
        };
        _fileDialog.FileSelected += path => _pendingFileCallback?.Invoke(path);
        AddChild(_fileDialog);

        UpdateVersionDependentUI();
    }

    #region Graphics Tab

    private Control BuildGraphicsTab()
    {
        var margin = CreateTabMargin();
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);

        vbox.AddChild(AppTheme.Heading("Convertir Graficos"));
        vbox.AddChild(AppTheme.MakeLabel("Convierte BMP sueltos o Graphics.AO a PNG con color key.", AppTheme.TEXT_SECONDARY));
        vbox.AddChild(AppTheme.Sep());

        _gfxIsArchive = new CheckBox { Text = "Es archivo Graphics.AO (0.12.3)" };
        _gfxIsArchive.AddThemeFontSizeOverride("font_size", AppTheme.FONT_MD);
        vbox.AddChild(_gfxIsArchive);

        // Input
        vbox.AddChild(AppTheme.SectionLabel("CARPETA / ARCHIVO DE ORIGEN"));
        var gfxInputRow = CreatePathRow(out _gfxInputPath, "Seleccionar...", () =>
        {
            if (_gfxIsArchive?.ButtonPressed == true)
            {
                _fileDialog!.ClearFilters();
                _fileDialog.AddFilter("*.AO;*.ao", "Graphics.AO archive");
                _pendingFileCallback = path => _gfxInputPath!.Text = path;
                _fileDialog.Popup();
            }
            else
            {
                _pendingDirCallback = path => _gfxInputPath!.Text = path;
                _dirDialog!.Popup();
            }
        });
        vbox.AddChild(gfxInputRow);

        // Output
        vbox.AddChild(AppTheme.SectionLabel("CARPETA DE SALIDA (PNGs)"));
        var gfxOutputRow = CreatePathRow(out _gfxOutputPath, "Seleccionar...", () =>
        {
            _pendingDirCallback = path => _gfxOutputPath!.Text = path;
            _dirDialog!.Popup();
        });
        vbox.AddChild(gfxOutputRow);

        // Progress
        _gfxProgress = AppTheme.MakeProgressBar();
        vbox.AddChild(_gfxProgress);

        _gfxStatus = AppTheme.MakeLabel("", AppTheme.TEXT_MUTED, AppTheme.FONT_SM);
        _gfxStatus.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        vbox.AddChild(_gfxStatus);

        // Convert button
        var convertBtn = AppTheme.PrimaryButton("Convertir Graficos");
        convertBtn.Pressed += OnConvertGraphics;
        vbox.AddChild(convertBtn);

        margin.AddChild(vbox);
        return margin;
    }

    private bool _converting; // prevents re-entry during async conversion

    private async void OnConvertGraphics()
    {
        string input = _gfxInputPath?.Text ?? "";
        string output = _gfxOutputPath?.Text ?? "";

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            SetGfxStatus("Seleccioná origen y destino", true);
            return;
        }
        if (_converting) return;
        _converting = true;

        bool isArchive = _gfxIsArchive?.ButtonPressed == true;
        SetGfxStatus("Convirtiendo...", false);
        _gfxProgress!.Value = 0;

        try
        {
            if (isArchive)
            {
                var result = await Task.Run(() =>
                    GraphicsAOExtractor.ExtractAndConvert(input, output,
                        (cur, total, name) => Callable.From(() =>
                        {
                            _gfxProgress.Value = (double)cur / total * 100;
                            _gfxStatus!.Text = $"[{cur}/{total}] {name}";
                        }).CallDeferred()));
                _gfxProgress.Value = 100;
                SetGfxStatus($"Listo: {result.Extracted} extraidos, {result.Errors} errores de {result.Total} total", false);
            }
            else
            {
                var result = await Task.Run(() =>
                    GraphicsConverter.Convert(input, output,
                        (cur, total, name) => Callable.From(() =>
                        {
                            _gfxProgress.Value = (double)cur / total * 100;
                            _gfxStatus!.Text = $"[{cur}/{total}] {name}";
                        }).CallDeferred()));
                _gfxProgress.Value = 100;
                SetGfxStatus($"Listo: {result.Converted} convertidos, {result.Skipped} omitidos, {result.Errors} errores de {result.Total} total", false);
            }
        }
        catch (Exception ex)
        {
            SetGfxStatus($"ERROR: {ex.Message}", true);
            GD.PrintErr($"[GFX] {ex}");
        }
        finally { _converting = false; }

        SetStatus("Conversión de gráficos completada");
    }

    private void SetGfxStatus(string text, bool isError)
    {
        if (_gfxStatus == null) return;
        _gfxStatus.Text = text;
        _gfxStatus.AddThemeColorOverride("font_color", isError ? AppTheme.TEXT_DANGER : AppTheme.TEXT_SUCCESS);
    }

    #endregion

    #region INITs Tab

    private Control BuildInitsTab()
    {
        var margin = CreateTabMargin();
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);

        vbox.AddChild(AppTheme.Heading("Convertir INITs"));
        vbox.AddChild(AppTheme.MakeLabel("Convierte archivos .ind y .dat de la carpeta INIT al formato NextGen.", AppTheme.TEXT_SECONDARY));
        vbox.AddChild(AppTheme.Sep());

        // Input
        vbox.AddChild(AppTheme.SectionLabel("CARPETA INIT DE ORIGEN"));
        var initInputRow = CreatePathRow(out _initInputPath, "Seleccionar...", () =>
        {
            _pendingDirCallback = path =>
            {
                _initInputPath!.Text = path;
                ValidateInits(path);
            };
            _dirDialog!.Popup();
        });
        vbox.AddChild(initInputRow);

        // Validation
        _initValidation = AppTheme.MakeLabel("Seleccioná una carpeta para validar archivos", AppTheme.TEXT_MUTED, AppTheme.FONT_SM);
        _initValidation.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _initValidation.CustomMinimumSize = new Vector2(0, 60);
        vbox.AddChild(_initValidation);

        // Output
        vbox.AddChild(AppTheme.SectionLabel("CARPETA INIT DE SALIDA"));
        var initOutputRow = CreatePathRow(out _initOutputPath, "Seleccionar...", () =>
        {
            _pendingDirCallback = path => _initOutputPath!.Text = path;
            _dirDialog!.Popup();
        });
        vbox.AddChild(initOutputRow);

        // Progress
        _initProgress = AppTheme.MakeProgressBar();
        vbox.AddChild(_initProgress);

        _initStatus = AppTheme.MakeLabel("", AppTheme.TEXT_MUTED, AppTheme.FONT_SM);
        vbox.AddChild(_initStatus);

        // Convert button
        var convertBtn = AppTheme.PrimaryButton("Convertir INITs");
        convertBtn.Pressed += OnConvertInits;
        vbox.AddChild(convertBtn);

        margin.AddChild(vbox);
        return margin;
    }

    private void ValidateInits(string dir)
    {
        if (_initValidation == null) return;

        var expected = VersionConfig.ExpectedInits(_selectedVersion);
        var lines = new System.Collections.Generic.List<string>();
        int found = 0;

        foreach (string file in expected)
        {
            bool exists = FindFileCI(dir, file);
            lines.Add(exists ? $"  [OK] {file}" : $"  [FALTA] {file}");
            if (exists) found++;
        }

        string header = $"Version {VersionConfig.Label(_selectedVersion)}: {found}/{expected.Length} archivos encontrados\n";
        _initValidation.Text = header + string.Join("\n", lines);
        _initValidation.AddThemeColorOverride("font_color",
            found == expected.Length ? AppTheme.TEXT_SUCCESS : AppTheme.TEXT_DANGER);
    }

    private async void OnConvertInits()
    {
        string input = _initInputPath?.Text ?? "";
        string output = _initOutputPath?.Text ?? "";

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            SetInitStatus("Seleccioná origen y destino", true);
            return;
        }
        if (_converting) return;
        _converting = true;

        SetInitStatus("Convirtiendo...", false);
        _initProgress!.Value = 0;

        try
        {
            var version = _selectedVersion;
            var result = await Task.Run(() =>
                InitConverter.Convert(input, output, version,
                    (cur, total, name) => Callable.From(() =>
                    {
                        _initProgress.Value = (double)cur / total * 100;
                        _initStatus!.Text = $"[{cur}/{total}] {name}";
                    }).CallDeferred()));
            _initProgress.Value = 100;

            string missingInfo = result.Missing.Length > 0
                ? $" (faltaron: {string.Join(", ", result.Missing)})"
                : "";
            SetInitStatus($"Listo: {result.Converted} convertidos, {result.Errors} errores{missingInfo}", result.Errors > 0);
        }
        catch (Exception ex)
        {
            SetInitStatus($"ERROR: {ex.Message}", true);
            GD.PrintErr($"[INIT] {ex}");
        }
        finally { _converting = false; }

        SetStatus("Conversión de INITs completada");
    }

    private void SetInitStatus(string text, bool isError)
    {
        if (_initStatus == null) return;
        _initStatus.Text = text;
        _initStatus.AddThemeColorOverride("font_color", isError ? AppTheme.TEXT_DANGER : AppTheme.TEXT_SUCCESS);
    }

    #endregion

    #region Maps Tab

    private Control BuildMapsTab()
    {
        var margin = CreateTabMargin();
        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 12);

        vbox.AddChild(AppTheme.Heading("Convertir Mapas"));
        vbox.AddChild(AppTheme.MakeLabel("Convierte todos los .map/.inf a .aomap/.aoinf (Int32, tamaño variable).", AppTheme.TEXT_SECONDARY));
        vbox.AddChild(AppTheme.Sep());

        // Input
        vbox.AddChild(AppTheme.SectionLabel("CARPETA MAPS DE ORIGEN"));
        var mapInputRow = CreatePathRow(out _mapInputPath, "Seleccionar...", () =>
        {
            _pendingDirCallback = path => _mapInputPath!.Text = path;
            _dirDialog!.Popup();
        });
        vbox.AddChild(mapInputRow);

        // Output
        vbox.AddChild(AppTheme.SectionLabel("CARPETA MAPS DE SALIDA"));
        var mapOutputRow = CreatePathRow(out _mapOutputPath, "Seleccionar...", () =>
        {
            _pendingDirCallback = path => _mapOutputPath!.Text = path;
            _dirDialog!.Popup();
        });
        vbox.AddChild(mapOutputRow);

        // Progress
        _mapProgress = AppTheme.MakeProgressBar();
        vbox.AddChild(_mapProgress);

        _mapStatus = AppTheme.MakeLabel("", AppTheme.TEXT_MUTED, AppTheme.FONT_SM);
        vbox.AddChild(_mapStatus);

        // Convert button
        var convertBtn = AppTheme.PrimaryButton("Convertir Mapas");
        convertBtn.Pressed += OnConvertMaps;
        vbox.AddChild(convertBtn);

        margin.AddChild(vbox);
        return margin;
    }

    private async void OnConvertMaps()
    {
        string input = _mapInputPath?.Text ?? "";
        string output = _mapOutputPath?.Text ?? "";

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output))
        {
            SetMapStatus("Seleccioná origen y destino", true);
            return;
        }
        if (_converting) return;
        _converting = true;

        SetMapStatus("Convirtiendo...", false);
        _mapProgress!.Value = 0;

        try
        {
            var version = _selectedVersion;
            var result = await Task.Run(() =>
                MapConverter.Convert(input, output, version,
                    (cur, total, name) => Callable.From(() =>
                    {
                        _mapProgress.Value = (double)cur / total * 100;
                        _mapStatus!.Text = $"[{cur}/{total}] {name}";
                    }).CallDeferred()));
            _mapProgress.Value = 100;
            SetMapStatus($"Listo: {result.Converted} convertidos, {result.Skipped} omitidos, {result.Errors} errores de {result.Total} total", result.Errors > 0);
        }
        catch (Exception ex)
        {
            SetMapStatus($"ERROR: {ex.Message}", true);
            GD.PrintErr($"[MAP] {ex}");
        }
        finally { _converting = false; }

        SetStatus("Conversión de mapas completada");
    }

    private void SetMapStatus(string text, bool isError)
    {
        if (_mapStatus == null) return;
        _mapStatus.Text = text;
        _mapStatus.AddThemeColorOverride("font_color", isError ? AppTheme.TEXT_DANGER : AppTheme.TEXT_SUCCESS);
    }

    #endregion

    #region Helpers

    private void OnVersionChanged(long index)
    {
        _selectedVersion = (AoVersion)_versionSelect!.GetSelectedId();
        UpdateVersionDependentUI();

        // Re-validate INITs if path is set
        string initPath = _initInputPath?.Text ?? "";
        if (!string.IsNullOrWhiteSpace(initPath) && Directory.Exists(initPath))
            ValidateInits(initPath);
    }

    private void UpdateVersionDependentUI()
    {
        if (_gfxIsArchive == null) return;
        bool usesArchive = VersionConfig.UsesGraphicsArchive(_selectedVersion);
        _gfxIsArchive.ButtonPressed = usesArchive;
        _gfxIsArchive.Disabled = !usesArchive;
    }

    private void SetStatus(string text)
    {
        if (_statusLabel != null) _statusLabel.Text = text;
    }

    private static MarginContainer CreateTabMargin()
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        return margin;
    }

    private static HBoxContainer CreatePathRow(out LineEdit pathEdit, string buttonText, Action onBrowse)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);

        pathEdit = new LineEdit();
        pathEdit.PlaceholderText = "/ruta/a/carpeta...";
        pathEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        pathEdit.AddThemeFontSizeOverride("font_size", AppTheme.FONT_MD);
        row.AddChild(pathEdit);

        var btn = AppTheme.MakeButton(buttonText);
        btn.Pressed += onBrowse;
        row.AddChild(btn);

        return row;
    }

    private static bool FindFileCI(string dir, string fileName)
    {
        if (File.Exists(Path.Combine(dir, fileName))) return true;
        try
        {
            foreach (var f in Directory.GetFiles(dir))
                if (string.Equals(Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"[FindFileCI] Error accessing {dir}: {ex.Message}");
        }
        return false;
    }

    #endregion
}
