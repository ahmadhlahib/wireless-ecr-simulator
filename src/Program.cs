// Wireless ECR Simulator - by Ahmad El Lahib
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

[assembly: System.Reflection.AssemblyTitle("Wireless ECR Simulator")]
[assembly: System.Reflection.AssemblyDescription("Merchant register and virtual POS terminal for the areeba wireless ECR integration server")]
[assembly: System.Reflection.AssemblyCompany("Ahmad El Lahib")]
[assembly: System.Reflection.AssemblyProduct("Wireless ECR Simulator")]
[assembly: System.Reflection.AssemblyCopyright("Ahmad El Lahib")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace WirelessEcrSim
{
    public static class Program
    {
        /// <summary>Folder holding settings, logs, assets and src (the exe folder, or the launcher's folder).</summary>
        public static string AppDir = AppDomain.CurrentDomain.BaseDirectory;

        [STAThread]
        public static int Main(string[] args)
        {
            return Run(AppDomain.CurrentDomain.BaseDirectory, args);
        }

        /// <summary>Entry point for the PowerShell launcher, which compiles the sources in memory (no exe on disk).</summary>
        public static int Run(string appDir, string[] args)
        {
            AppDir = appDir;
            Application app = Application.Current ?? new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.DispatcherUnhandledException += (s, e) =>
            {
                MessageBox.Show(e.Exception.ToString(), "Wireless ECR Simulator - unexpected error", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
            };
            MainController controller;
            try
            {
                controller = new MainController();
            }
            catch (Exception e)
            {
                MessageBox.Show(e.ToString(), "Wireless ECR Simulator could not start", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }

            // --snapshot file.png renders the window to an image and exits (for docs and checks)
            if (Array.IndexOf(args, "--demo") >= 0) controller.ShowDemoResult();
            int snap = Array.IndexOf(args, "--snapshot");
            if (snap >= 0 && snap + 1 < args.Length)
            {
                string file = args[snap + 1];
                controller.Window.ContentRendered += delegate
                {
                    controller.Window.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        Snapshot(controller.Window, file);
                        controller.Window.Close();
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };
            }
            return app.Run(controller.Window);
        }

        private static void Snapshot(Window w, string file)
        {
            FrameworkElement root = (FrameworkElement)w.Content;
            int width = (int)root.ActualWidth, height = (int)root.ActualHeight;
            RenderTargetBitmap bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(w.Background, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, width, height));
            }
            bmp.Render(dv);
            PngBitmapEncoder enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream fs = File.Create(file)) enc.Save(fs);
        }
    }
}
