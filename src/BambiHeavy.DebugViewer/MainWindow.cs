using System;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BambiHeavy.Capture;
using BambiHeavy.Edge;
using SkiaSharp;

namespace BambiHeavy.DebugViewer;

public class MainWindow : Window
{
    private readonly Button _captureButton;
    private readonly TextBlock _statusText;
    private readonly Image _screenshotImage;
    private readonly Grid _overlayGrid;
    private readonly Canvas _tilesCanvas;
    private readonly Canvas _edgesCanvas;
    private readonly Rectangle _boundsRect;
    private readonly Viewbox _viewbox;
    
    private readonly CheckBox _toggleTiles;
    private readonly CheckBox _toggleBounds;
    private readonly CheckBox _toggleEdges;

    private readonly StackPanel _infoPanel;
    private readonly StackPanel _tileDetailsPanel;
    private Rectangle? _selectedTileRect;
    private string? _selectedEdgeHighlight;
    private Action? _updateTileHighlights;

    public MainWindow()
    {
        Width = 1400;
        Height = 900;
        Title = "ColorExtractor Debug Viewer";
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        Closing += (_, _) =>
        {
            if (Application.Current is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        };

        // UI Setup
        _captureButton = new Button
        {
            Content = "Capture Frame",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.Parse("#007ACC")),
            Foreground = Brushes.White,
            FontWeight = FontWeight.Bold
        };
        _captureButton.Click += CaptureButton_Click;

        _statusText = new TextBlock
        {
            Text = "Ready to capture. You can also drag & drop images here.",
            Foreground = new SolidColorBrush(Colors.LightGray),
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };

        _toggleBounds = new CheckBox { Content = "Show Content Bounds", IsChecked = true, Margin = new Thickness(0, 0, 0, 5) };
        _toggleTiles = new CheckBox { Content = "Show Sampled Tiles", IsChecked = true, Margin = new Thickness(0, 0, 0, 5) };
        _toggleEdges = new CheckBox { Content = "Show Edge Colors", IsChecked = true, Margin = new Thickness(0, 0, 0, 15) };

        _infoPanel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 10, 0, 0) };
        _tileDetailsPanel = new StackPanel { Spacing = 10, Margin = new Thickness(0, 20, 0, 0) };

        var controlsBorder = new Border 
        { 
            Padding = new Thickness(15), 
            Background = new SolidColorBrush(Color.Parse("#333333")),
            Child = new StackPanel 
            {
                Children = { _captureButton, _statusText, _toggleBounds, _toggleTiles, _toggleEdges }
            }
        };

        var scrollViewer = new ScrollViewer
        {
            Padding = new Thickness(15),
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel { Children = { _infoPanel, _tileDetailsPanel } }
        };

        var sidebar = new Grid
        {
            Width = 350,
            Background = new SolidColorBrush(Color.Parse("#252526")),
            Margin = new Thickness(0),
            RowDefinitions = new RowDefinitions("Auto,*"),
            Children = { controlsBorder, scrollViewer }
        };
        Grid.SetRow(controlsBorder, 0);
        Grid.SetRow(scrollViewer, 1);

        _screenshotImage = new Image { Stretch = Stretch.Fill, IsHitTestVisible = false };

        _boundsRect = new Rectangle
        {
            Stroke = new SolidColorBrush(Colors.Yellow),
            StrokeThickness = 3,
            IsHitTestVisible = false,
            IsVisible = true
        };

        _tilesCanvas = new Canvas { IsHitTestVisible = true, Background = Brushes.Transparent };
        _edgesCanvas = new Canvas { IsHitTestVisible = true };

        _toggleBounds.IsCheckedChanged += (_, _) => _boundsRect.IsVisible = _toggleBounds.IsChecked == true;
        _toggleTiles.IsCheckedChanged += (_, _) => _tilesCanvas.IsVisible = _toggleTiles.IsChecked == true;
        _toggleEdges.IsCheckedChanged += (_, _) => _edgesCanvas.IsVisible = _toggleEdges.IsChecked == true;

        _overlayGrid = new Grid
        {
            Background = Brushes.Transparent,
            Children = { _screenshotImage, _tilesCanvas, _boundsRect, _edgesCanvas }
        };
        
        _overlayGrid.PointerPressed += (s, e) =>
        {
            // If the user clicked the background, clear selection
            if (e.Source == _overlayGrid || e.Source == _tilesCanvas || e.Source == _edgesCanvas)
            {
                _selectedTileRect = null;
                _selectedEdgeHighlight = null;
                _updateTileHighlights?.Invoke();
            }
        };

        _viewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(15),
            Child = _overlayGrid
        };

        var mainPanel = new Grid
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E1E")),
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                _viewbox,
                sidebar
            }
        };

        Grid.SetColumn(_viewbox, 0);
        Grid.SetColumn(sidebar, 1);

        Content = mainPanel;

        // Setup Drag & Drop
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DropEvent, DropHandler);
    }

    private async void DropHandler(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Formats.Contains(DataFormat.File))
        {
            foreach (var item in e.DataTransfer.Items)
            {
                if (item.TryGetRaw(DataFormat.File) is IStorageFile file)
                {
                    var path = file.Path.LocalPath;
                    if (!string.IsNullOrEmpty(path))
                    {
                        await LoadImageAsync(path);
                        break; // Only load the first dropped image
                    }
                }
            }
        }
    }

    private async Task LoadImageAsync(string path)
    {
        _captureButton.IsEnabled = false;
        _statusText.Text = $"Loading {System.IO.Path.GetFileName(path)}...";

        try
        {
            await Task.Run(() => 
            {
                using var stream = System.IO.File.OpenRead(path);
                using var skBitmap = SKBitmap.Decode(stream);
                
                if (skBitmap == null) throw new Exception("Failed to decode image. Is it a valid image file?");
                
                SKBitmap bgraBitmap = skBitmap;
                bool needDisposeBgra = false;
                
                if (skBitmap.ColorType != SKColorType.Bgra8888)
                {
                    bgraBitmap = skBitmap.Copy(SKColorType.Bgra8888);
                    needDisposeBgra = true;
                }

                int w = bgraBitmap.Width;
                int h = bgraBitmap.Height;
                int stride = bgraBitmap.RowBytes;
                
                byte[] pixels = new byte[h * stride];
                System.Runtime.InteropServices.Marshal.Copy(bgraBitmap.GetPixels(), pixels, 0, pixels.Length);
                
                if (needDisposeBgra) bgraBitmap.Dispose();

                var frame = new ScreenCaptureResult(w, h, stride, pixels);
                var bounds = new BambiHeavy.Types.Rectangle(0, 0, w, h);
                
                ContentBoundsDetector.Reset();
                var contentBounds = ContentBoundsDetector.Detect(frame, bounds);
                var colorGrid = TileColorExtractor.Extract(frame, bounds, contentBounds);
                var debugInfo = ReadTileDebugInfo();

                var avaloniaBitmap = CreateBitmap(frame, w, h);
                
                Dispatcher.UIThread.Post(() =>
                {
                    UpdateView(avaloniaBitmap, w, h, contentBounds, colorGrid, debugInfo);
                    _statusText.Text = $"Loaded {System.IO.Path.GetFileName(path)} successfuly.";
                });
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DebugViewer] Error: {ex}");
            _statusText.Text = $"Error loading image: {ex.Message}";
        }
        finally
        {
            _captureButton.IsEnabled = true;
        }
    }

    private void CaptureButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = CaptureAsync();
    }

    private async Task CaptureAsync()
    {
        _captureButton.IsEnabled = false;
        _statusText.Text = "Hiding window to capture...";
        Console.WriteLine("[DebugViewer] Hiding window...");

        Hide();

        await Task.Delay(300); // Wait for window to disappear
        await Task.Delay(2000); // Give user time to see what's being captured

        _statusText.Text = "Capturing...";
        Console.WriteLine("[DebugViewer] Capturing frames...");

        try
        {
            ContentBoundsDetector.Reset();
            var capture = new WindowsScreenCapture();
            var bounds = capture.GetPrimaryScreenBounds();
            Console.WriteLine($"[DebugViewer] Screen bounds: {bounds.Width}x{bounds.Height}");

            ScreenCaptureResult frame = default;
            // Warm up pipeline and get settled frame
            for (var i = 0; i < 5; i++)
            {
                frame = capture.Capture(bounds, 100);
                await Task.Delay(50);
            }

            capture.Dispose();
            Console.WriteLine($"[DebugViewer] Got {frame.Pixels.Length} bytes");

            var contentBounds = ContentBoundsDetector.Detect(frame, bounds);
            var colorGrid = TileColorExtractor.Extract(frame, bounds, contentBounds);
            Console.WriteLine($"[DebugViewer] Content bounds: {contentBounds.left},{contentBounds.top} - {contentBounds.right},{contentBounds.bottom}");
            var debugInfo = ReadTileDebugInfo();

            var bitmap = CreateBitmap(frame, bounds.Width, bounds.Height);
            frame.Dispose();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateView(bitmap, bounds.Width, bounds.Height, contentBounds, colorGrid, debugInfo);
                _statusText.Text = "Capture successful.";
            });

            Show();
            Console.WriteLine("[DebugViewer] Done");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DebugViewer] Error: {ex}");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _statusText.Text = $"Error: {ex.Message}";
            });
            Show();
        }
        finally
        {
            _captureButton.IsEnabled = true;
        }
    }

    private WriteableBitmap CreateBitmap(ScreenCaptureResult frame, int w, int h)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(w, h),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Opaque);

        using var buf = bitmap.Lock();
        for (var y = 0; y < h; y++)
            unsafe
            {
                var dst = new Span<byte>((void*)((byte*)buf.Address + y * buf.RowBytes), w * 4);
                var src = frame.Pixels.AsSpan().Slice(y * frame.Stride, w * 4);
                src.CopyTo(dst);
            }

        return bitmap;
    }

    private void UpdateView(WriteableBitmap bitmap, int w, int h,
        (int left, int top, int right, int bottom) contentBounds, ColorGrid colorGrid, TileDebugInfo debugInfo)
    {
        _screenshotImage.Source = bitmap;

        _overlayGrid.Width = w;
        _overlayGrid.Height = h;

        // Update bounds rectangle
        _boundsRect.Margin = new Thickness(contentBounds.left, contentBounds.top,
            w - contentBounds.right, h - contentBounds.bottom);

        // Update tiles canvas
        _tilesCanvas.Children.Clear();

        var cLeft = contentBounds.left;
        var cTop = contentBounds.top;
        var cRight = contentBounds.right;
        var cBottom = contentBounds.bottom;

        var tileRects = new System.Collections.Generic.List<(Rectangle Rect, EdgeBandInfo Edge, int Idx, int Col, int Row, string EdgeName)>();
        var edgeRects = new System.Collections.Generic.Dictionary<string, Rectangle>();

        void ProcessEdgeBand(EdgeBandInfo edge, string edgeName)
        {
            if (edge.Cols <= 0 || edge.Rows <= 0) return;
            
            var tileW = (double)(edge.BandRight - edge.BandLeft) / edge.Cols;
            var tileH = (double)(edge.BandBottom - edge.BandTop) / edge.Rows;

            for (var row = 0; row < edge.Rows; row++)
            {
                for (var col = 0; col < edge.Cols; col++)
                {
                    var idx = row * edge.Cols + col;
                    var tile = edge.Tiles[idx];

                    var alpha = (byte)(Math.Max(0.2, tile.Factor) * 200);

                    var rect = new Rectangle
                    {
                        Width = tileW,
                        Height = tileH,
                        Fill = new SolidColorBrush(Color.FromArgb(alpha, tile.R, tile.G, tile.B)),
                        Stroke = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)),
                        StrokeThickness = 1,
                        IsHitTestVisible = true,
                        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
                    };

                    var localCol = col;
                    var localRow = row;
                    var localIdx = idx;
                    var localEdge = edge;
                    var localEdgeName = edgeName;

                    rect.PointerPressed += (s, e) =>
                    {
                        _selectedTileRect = rect;
                        _updateTileHighlights?.Invoke();
                        ShowTileDetails(localCol, localRow, localIdx, localEdge, localEdgeName);
                        e.Handled = true;
                    };

                    Canvas.SetLeft(rect, edge.BandLeft + col * tileW);
                    Canvas.SetTop(rect, edge.BandTop + row * tileH);

                    _tilesCanvas.Children.Add(rect);
                    tileRects.Add((rect, edge, idx, col, row, edgeName));
                }
            }
        }

        ProcessEdgeBand(debugInfo.TopEdge, "Top");
        ProcessEdgeBand(debugInfo.BottomEdge, "Bottom");
        ProcessEdgeBand(debugInfo.LeftEdge, "Left");
        ProcessEdgeBand(debugInfo.RightEdge, "Right");

        // Update edges canvas
        _edgesCanvas.Children.Clear();

        var edgeSize = 40;
        var topAvgColor = colorGrid.Top.ContentAverage();
        var botAvgColor = colorGrid.Bottom.ContentAverage();
        var leftAvgColor = colorGrid.Left.ContentAverage();
        var rightAvgColor = colorGrid.Right.ContentAverage();

        var edges = new[]
        {
            ("Top", topAvgColor, cLeft + (cRight - cLeft) / 2, cTop - edgeSize / 2 - 10),
            ("Bottom", botAvgColor, cLeft + (cRight - cLeft) / 2, cBottom + 10),
            ("Left", leftAvgColor, cLeft - edgeSize / 2 - 10, cTop + (cBottom - cTop) / 2),
            ("Right", rightAvgColor, cRight + edgeSize / 2 + 10, cTop + (cBottom - cTop) / 2)
        };

        foreach (var (edgeName, c, cx, cy) in edges)
        {
            var rect = new Rectangle
            {
                Width = edgeSize,
                Height = edgeSize,
                Fill = new SolidColorBrush(Color.FromRgb(c.R, c.G, c.B)),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                RadiusX = 5,
                RadiusY = 5,
                IsHitTestVisible = true,
                Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
            };
            
            var localEdgeName = edgeName;
            rect.PointerPressed += (s, e) =>
            {
                if (_selectedEdgeHighlight == localEdgeName) _selectedEdgeHighlight = null;
                else _selectedEdgeHighlight = localEdgeName;
                _updateTileHighlights?.Invoke();
                e.Handled = true;
            };

            var leftPos = Math.Clamp(cx - edgeSize / 2.0, 10, w - edgeSize - 10);
            var topPos = Math.Clamp(cy - edgeSize / 2.0, 10, h - edgeSize - 10);
            
            Canvas.SetLeft(rect, leftPos);
            Canvas.SetTop(rect, topPos);
            _edgesCanvas.Children.Add(rect);
            edgeRects[edgeName] = rect;
        }

        _updateTileHighlights = () =>
        {
            bool anyEdgeSelected = _selectedEdgeHighlight != null;
            if (!anyEdgeSelected)
            {
                _selectedTileRect = null;
            }

            foreach (var t in tileRects)
            {
                var tile = t.Edge.Tiles[t.Idx];

                if (anyEdgeSelected)
                {
                    if (t.EdgeName == _selectedEdgeHighlight)
                    {
                        double dist = _selectedEdgeHighlight switch 
                        { 
                            "Top" => (t.Row + 0.5) / t.Edge.Rows, 
                            "Bottom" => 1.0 - (t.Row + 0.5) / t.Edge.Rows, 
                            "Left" => (t.Col + 0.5) / t.Edge.Cols, 
                            "Right" => 1.0 - (t.Col + 0.5) / t.Edge.Cols, 
                            _ => 0 
                        };
                        double prox = Math.Pow(1.0 - dist, 2);
                        
                        if (prox >= 0.01)
                        {
                            var alpha = (byte)Math.Clamp((prox * tile.Factor) * 255, 30, 255);
                            t.Rect.Fill = new SolidColorBrush(Color.FromArgb(alpha, tile.R, tile.G, tile.B));
                            t.Rect.Stroke = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
                            t.Rect.IsHitTestVisible = true;
                        }
                        else
                        {
                            t.Rect.Fill = new SolidColorBrush(Color.FromArgb(10, tile.R, tile.G, tile.B));
                            t.Rect.Stroke = Brushes.Transparent;
                            t.Rect.IsHitTestVisible = false;
                        }
                    }
                    else
                    {
                        t.Rect.Fill = Brushes.Transparent;
                        t.Rect.Stroke = Brushes.Transparent;
                        t.Rect.IsHitTestVisible = false;
                    }
                }
                else
                {
                    // No edge selected: show as fuzzy heatmap without borders to avoid grid clash
                    var alpha = (byte)(Math.Max(0.1, tile.Factor) * 150);
                    t.Rect.Fill = new SolidColorBrush(Color.FromArgb(alpha, tile.R, tile.G, tile.B));
                    t.Rect.Stroke = Brushes.Transparent;
                    t.Rect.IsHitTestVisible = false;
                }
                
                if (_selectedTileRect == t.Rect)
                {
                    t.Rect.Stroke = Brushes.Yellow;
                    t.Rect.StrokeThickness = 2;
                }
                else
                {
                    t.Rect.StrokeThickness = 1;
                }
            }
            
            foreach (var kvp in edgeRects)
            {
                if (_selectedEdgeHighlight == kvp.Key)
                {
                    kvp.Value.Stroke = Brushes.Yellow;
                    kvp.Value.StrokeThickness = 4;
                }
                else
                {
                    kvp.Value.Stroke = Brushes.White;
                    kvp.Value.StrokeThickness = 2;
                }
            }

            if (!anyEdgeSelected)
            {
                _tileDetailsPanel.Children.Clear();
                _tileDetailsPanel.Children.Add(new TextBlock
                {
                    Text = "Select an edge indicator (Top, Bottom, Left, Right) to view and inspect its sampled tiles.",
                    Foreground = new SolidColorBrush(Color.Parse("#888888")),
                    FontStyle = FontStyle.Italic,
                    Margin = new Thickness(0, 20, 15, 0),
                    TextWrapping = TextWrapping.Wrap
                });
            }
        };

        _updateTileHighlights();
        
        // Update text info
        ShowData(colorGrid, contentBounds, debugInfo);
    }

    private TileDebugInfo ReadTileDebugInfo()
    {
        var type = typeof(TileColorExtractor);
        var binding = BindingFlags.NonPublic | BindingFlags.Static;

        var tileR = (float[])type.GetField("_tileR", binding)!.GetValue(null)!;
        var tileG = (float[])type.GetField("_tileG", binding)!.GetValue(null)!;
        var tileB = (float[])type.GetField("_tileB", binding)!.GetValue(null)!;
        var tileBright = (float[])type.GetField("_tileBright", binding)!.GetValue(null)!;
        var tileFactor = (float[])type.GetField("_tileFactor", binding)!.GetValue(null)!;

        var colsArr = (int[])type.GetField("_cols", binding)!.GetValue(null)!;
        var rowsArr = (int[])type.GetField("_rows", binding)!.GetValue(null)!;
        var bandLeftArr = (int[])type.GetField("_bandLeft", binding)!.GetValue(null)!;
        var bandTopArr = (int[])type.GetField("_bandTop", binding)!.GetValue(null)!;
        var bandRightArr = (int[])type.GetField("_bandRight", binding)!.GetValue(null)!;
        var bandBottomArr = (int[])type.GetField("_bandBottom", binding)!.GetValue(null)!;

        EdgeBandInfo ReadEdge(int idx)
        {
            var cols = colsArr[idx];
            var rows = rowsArr[idx];
            var bLeft = bandLeftArr[idx];
            var bTop = bandTopArr[idx];
            var bRight = bandRightArr[idx];
            var bBottom = bandBottomArr[idx];

            var total = cols * rows;
            var offset = idx * 128; // TilesPerEdge
            var tiles = new TileData[total];

            for (var i = 0; i < total; i++)
            {
                var ii = offset + i;
                tiles[i] = new TileData
                {
                    R = (byte)Math.Clamp(tileR[ii], 0, 255),
                    G = (byte)Math.Clamp(tileG[ii], 0, 255),
                    B = (byte)Math.Clamp(tileB[ii], 0, 255),
                    Brightness = tileBright[ii],
                    Factor = tileFactor[ii]
                };
            }

            return new EdgeBandInfo
            {
                Cols = cols,
                Rows = rows,
                BandLeft = bLeft,
                BandTop = bTop,
                BandRight = bRight,
                BandBottom = bBottom,
                Tiles = tiles
            };
        }

        return new TileDebugInfo
        {
            LeftEdge = ReadEdge(0),
            RightEdge = ReadEdge(1),
            TopEdge = ReadEdge(2),
            BottomEdge = ReadEdge(3)
        };
    }

    private void ShowData(ColorGrid grid,
        (int left, int top, int right, int bottom) contentBounds, TileDebugInfo debugInfo)
    {
        _infoPanel.Children.Clear();
        _tileDetailsPanel.Children.Clear();
        _selectedTileRect = null;
        _selectedEdgeHighlight = null;

        var contentW = contentBounds.right - contentBounds.left;
        var contentH = contentBounds.bottom - contentBounds.top;
        var ar = contentH == 0 ? 0 : (double)contentW / contentH;

        AddSectionHeader(_infoPanel, "Screen & Content");
        AddInfoRow(_infoPanel, "Content Bounds", $"{contentW} x {contentH}");
        AddInfoRow(_infoPanel, "Aspect Ratio", $"{ar:F2} : 1");

        AddSectionHeader(_infoPanel, "Edge Colors");
        var topAvg = grid.Top.ContentAverage();
        AddColorRow(_infoPanel, "Top Edge", topAvg.R, topAvg.G, topAvg.B);
        var botAvg = grid.Bottom.ContentAverage();
        AddColorRow(_infoPanel, "Bottom Edge", botAvg.R, botAvg.G, botAvg.B);
        var leftAvg = grid.Left.ContentAverage();
        AddColorRow(_infoPanel, "Left Edge", leftAvg.R, leftAvg.G, leftAvg.B);
        var rightAvg = grid.Right.ContentAverage();
        AddColorRow(_infoPanel, "Right Edge", rightAvg.R, rightAvg.G, rightAvg.B);
        
        AddSectionHeader(_infoPanel, "Grid Stats");
        var suppressedTiles = debugInfo.LeftEdge.Tiles.Count(t => t.Factor < 0.1f) +
                              debugInfo.RightEdge.Tiles.Count(t => t.Factor < 0.1f) +
                              debugInfo.TopEdge.Tiles.Count(t => t.Factor < 0.1f) +
                              debugInfo.BottomEdge.Tiles.Count(t => t.Factor < 0.1f);
        var totalTiles = debugInfo.LeftEdge.Tiles.Length +
                         debugInfo.RightEdge.Tiles.Length +
                         debugInfo.TopEdge.Tiles.Length +
                         debugInfo.BottomEdge.Tiles.Length;
        AddInfoRow(_infoPanel, "Suppressed Outliers", $"{suppressedTiles} / {totalTiles}");
    }

    private void ShowTileDetails(int col, int row, int idx, EdgeBandInfo edge, string edgeName)
    {
        _tileDetailsPanel.Children.Clear();
        var tile = edge.Tiles[idx];

        AddSectionHeader(_tileDetailsPanel, $"Selected Tile [{col}, {row}] in {edgeName}");
        AddColorRow(_tileDetailsPanel, "Raw Color", tile.R, tile.G, tile.B);
        AddInfoRow(_tileDetailsPanel, "Brightness", $"{tile.Brightness:F2}");
        AddInfoRow(_tileDetailsPanel, "Reinforcement", $"{tile.Factor:F3}");

        double dist = edgeName switch 
        { 
            "Left" => (col + 0.5) / edge.Cols,
            "Right" => 1.0 - (col + 0.5) / edge.Cols,
            "Top" => (row + 0.5) / edge.Rows,
            "Bottom" => 1.0 - (row + 0.5) / edge.Rows,
            _ => 0 
        };
        
        double proximity = Math.Pow(1.0 - dist, 2);
        if (proximity < 0.01) proximity = 0;

        AddSectionHeader(_tileDetailsPanel, "Edge Weight (Raw / Final)");
        
        double finalWeight = proximity * tile.Factor;
        AddInfoRow(_tileDetailsPanel, $"{edgeName} Edge", $"{proximity:F3}  /  {finalWeight:F3}");
    }

    private void AddSectionHeader(StackPanel panel, string title)
    {
        panel.Children.Add(new TextBlock
        {
            Text = title.ToUpperInvariant(),
            Foreground = new SolidColorBrush(Color.Parse("#007ACC")),
            FontWeight = FontWeight.Bold,
            FontSize = 12,
            Margin = new Thickness(0, 15, 0, 5)
        });
    }

    private void AddInfoRow(StackPanel panel, string label, string value)
    {
        var rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };

        rowGrid.Children.Add(new TextBlock
        {
            Text = label + ":",
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            Width = 120
        });

        var valueText = new TextBlock
        {
            Text = value,
            Foreground = Brushes.White,
            FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(valueText, 1);
        rowGrid.Children.Add(valueText);

        panel.Children.Add(rowGrid);
    }

    private void AddColorRow(StackPanel panel, string label, byte r, byte g, byte b)
    {
        var rowGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto")
        };

        rowGrid.Children.Add(new TextBlock
        {
            Text = label + ":",
            Foreground = new SolidColorBrush(Color.Parse("#888888")),
            Width = 100,
            VerticalAlignment = VerticalAlignment.Center
        });

        var hexText = new TextBlock
        {
            Text = $"#{r:X2}{g:X2}{b:X2}  (R:{r} G:{g} B:{b})",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0)
        };
        Grid.SetColumn(hexText, 1);
        rowGrid.Children.Add(hexText);

        var colorBox = new Border
        {
            Width = 24,
            Height = 24,
            Background = new SolidColorBrush(Color.FromRgb(r, g, b)),
            BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        Grid.SetColumn(colorBox, 2);
        rowGrid.Children.Add(colorBox);

        panel.Children.Add(rowGrid);
    }
}

internal struct TileData
{
    public byte R;
    public byte G;
    public byte B;
    public float Brightness;
    public float Factor;
}

internal class TileDebugInfo
{
    public EdgeBandInfo LeftEdge { get; set; } = new();
    public EdgeBandInfo RightEdge { get; set; } = new();
    public EdgeBandInfo TopEdge { get; set; } = new();
    public EdgeBandInfo BottomEdge { get; set; } = new();
}

internal class EdgeBandInfo
{
    public int Cols { get; set; }
    public int Rows { get; set; }
    public int BandLeft { get; set; }
    public int BandTop { get; set; }
    public int BandRight { get; set; }
    public int BandBottom { get; set; }
    public TileData[] Tiles { get; set; } = Array.Empty<TileData>();
}
