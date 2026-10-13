using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZPulse.Services;

namespace ZPulse;

public partial class MainWindow : Window
{
    private readonly SystemMetricsService _metricsService;
    private readonly DispatcherTimer _timer;
    private bool _isCompactMode = true; // Inicia no modo compacto ultra-slim por padrão

    private static readonly SolidColorBrush HighAlertBrush = new(Color.FromRgb(239, 68, 68));    // Vermelho (#EF4444)
    private static readonly SolidColorBrush MidAlertBrush = new(Color.FromRgb(245, 158, 11));     // Laranja (#F59E0B)
    private static readonly SolidColorBrush CpuNormalBrush = new(Color.FromRgb(96, 165, 250));    // Azul (#60A5FA)
    private static readonly SolidColorBrush MemNormalBrush = new(Color.FromRgb(167, 139, 250));   // Roxo (#A78BFA)
    private static readonly SolidColorBrush DiskNormalBrush = new(Color.FromRgb(52, 211, 153));   // Verde (#34D399)
    private static readonly SolidColorBrush PinActiveBrush = new(Color.FromRgb(96, 165, 250));
    private static readonly SolidColorBrush PinInactiveBrush = new(Color.FromRgb(156, 163, 175));

    public MainWindow()
    {
        InitializeComponent();

        _metricsService = new SystemMetricsService();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;
        _timer.Start();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Define a versão atual dinamicamente com base no assembly
        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        TxtCardVersion.Text = ver != null ? $"v{ver.Major}.{ver.Minor}.{ver.Build}" : "v1.0.0";

        // Aplica modo compacto inicial (apenas 46px de altura!)
        ApplyLayoutMode(_isCompactMode);

        // Posiciona no canto superior direito da tela de trabalho
        var screenWidth = SystemParameters.WorkArea.Width;
        Left = screenWidth - Width - 30;
        Top = 20;

        UpdateMetrics();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        UpdateMetrics();
    }

    private void UpdateMetrics()
    {
        var data = _metricsService.ReadMetrics();

        // 1. CPU
        int cpuInt = (int)Math.Round(data.CpuPercentage);
        var cpuBrush = cpuInt switch
        {
            > 85 => HighAlertBrush,
            > 70 => MidAlertBrush,
            _ => CpuNormalBrush
        };

        TxtCompactCpu.Text = $"{cpuInt}%";
        TxtCompactCpu.ToolTip = $"Uso de CPU: {cpuInt}%\n(Sincronizado com o Gerenciador de Tarefas)";
        BarCompactCpu.Value = cpuInt;
        BarCompactCpu.Foreground = cpuBrush;

        TxtCardCpu.Text = $"{cpuInt}%";
        TxtCardCpu.ToolTip = $"Uso de CPU: {cpuInt}%\n(Sincronizado com o Gerenciador de Tarefas)";
        BarCardCpu.Value = cpuInt;
        BarCardCpu.Foreground = cpuBrush;

        // 2. Memória
        int memInt = (int)Math.Round(data.MemoryPercentage);
        var memBrush = memInt switch
        {
            > 85 => HighAlertBrush,
            > 75 => MidAlertBrush,
            _ => MemNormalBrush
        };

        TxtCompactMem.Text = $"{memInt}%";
        BarCompactMem.Value = memInt;
        BarCompactMem.Foreground = memBrush;

        TxtCardMem.Text = $"{memInt}%";
        TxtCardMemSub.Text = $"{data.UsedMemoryGb:0.0} / {data.TotalMemoryGb:0.0} GB";
        BarCardMem.Value = memInt;
        BarCardMem.Foreground = memBrush;

        // 3. Disco
        int diskInt = (int)Math.Round(data.DiskPercentage);
        var diskBrush = diskInt switch
        {
            > 85 => HighAlertBrush,
            > 70 => MidAlertBrush,
            _ => DiskNormalBrush
        };

        TxtCompactDisk.Text = $"{diskInt}%";
        BarCompactDisk.Value = diskInt;
        BarCompactDisk.Foreground = diskBrush;

        TxtCardDisk.Text = $"{diskInt}%";
        TxtCardDiskSub.Text = data.DiskDetails;
        BarCardDisk.Value = diskInt;
        BarCardDisk.Foreground = diskBrush;
    }

    private void ApplyLayoutMode(bool isCompact)
    {
        _isCompactMode = isCompact;

        if (_isCompactMode)
        {
            // Barra horizontal ultra-compacta (apenas 46px de altura total)
            MinHeight = 38;
            MaxHeight = 60;
            Height = 46;

            MinWidth = 280;
            MaxWidth = 600;
            Width = 380;

            CompactLayout.Visibility = Visibility.Visible;
            CardLayout.Visibility = Visibility.Collapsed;
        }
        else
        {
            // Modo Card vertical completo com rodapé
            MinHeight = 180;
            MaxHeight = 500;
            Height = 225;

            MinWidth = 190;
            MaxWidth = 450;
            Width = 240;

            CompactLayout.Visibility = Visibility.Collapsed;
            CardLayout.Visibility = Visibility.Visible;
        }
    }

    private void ToggleMode_Click(object sender, RoutedEventArgs e)
    {
        ApplyLayoutMode(!_isCompactMode);
    }

    // Duplo clique na janela alterna entre o modo compacto e o modo card
    private void Window_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            ApplyLayoutMode(!_isCompactMode);
            e.Handled = true;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ToggleTopmost_Click(object sender, RoutedEventArgs e)
    {
        Topmost = !Topmost;
        var brush = Topmost ? PinActiveBrush : PinInactiveBrush;
        BtnCompactPin.Foreground = brush;
        BtnCardPin.Foreground = brush;
    }

    private void SetOpacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tagStr } && double.TryParse(tagStr, System.Globalization.CultureInfo.InvariantCulture, out double op))
        {
            Opacity = op;
        }
    }

    private void ResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        double newWidth = Width + e.HorizontalChange;
        double newHeight = Height + e.VerticalChange;

        if (newWidth >= MinWidth && newWidth <= MaxWidth)
        {
            Width = newWidth;
        }

        if (newHeight >= MinHeight && newHeight <= MaxHeight)
        {
            Height = newHeight;
        }
    }

    private void OpenTaskManager_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Não foi possível abrir o Gerenciador de Tarefas: {ex.Message}", "ZPulse", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
        _metricsService.Dispose();
    }
}