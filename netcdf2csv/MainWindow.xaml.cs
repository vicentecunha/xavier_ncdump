using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace xavier_ncdump
{
    /// <summary>
    /// Interação lógica para MainWindow.xam
    /// </summary>
    public partial class MainWindow : Window
    {
        //=========================//
        //=== PRIVATE VARIABLES ===//
        //=========================//

        private double[] latitude, longitude, time;
        private double?[,,] data;
        private TextBox fileTB, kindTB, headerTB;
        private Button importBTN, exportCSV_BTN, exportTXT_BTN;
        private TextBlock statusTB;
        private string importFilename, exportFilename, ft, fh, varName;
        private static ManualResetEvent manualEvent_import = new ManualResetEvent(false);
        private static ManualResetEvent manualEvent_export = new ManualResetEvent(false);
        private Process process = new Process();

        //=======================//
        //=== INITIALIZATIONS ===//
        //=======================//

        public MainWindow()
        {
            InitializeComponent();
        }

        private void fileTB_loaded(object sender, RoutedEventArgs e)
        {
            fileTB = (TextBox)sender;
        }

        private void kindTB_loaded(object sender, RoutedEventArgs e)
        {
            kindTB = (TextBox)sender;
        }

        private void headerTB_loaded(object sender, RoutedEventArgs e)
        {
            headerTB = (TextBox)sender;
        }

        private void importBTN_loaded(object sender, RoutedEventArgs e)
        {
            importBTN = (Button)sender;
        }

        private void exportCSV_BTN_loaded(object sender, RoutedEventArgs e)
        {
            exportCSV_BTN = (Button)sender;
        }

        private void exportTXT_BTN_loaded(object sender, RoutedEventArgs e)
        {
            exportTXT_BTN = (Button)sender;
        }

        private void exportTB_loaded(object sender, RoutedEventArgs e)
        {
            statusTB = (TextBlock)sender;
        }

        //=========================//
        //=== PRIVATE FUNCTIONS ===//
        //=========================//

        private string inputFile()
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "NetCDF|*.nc";
            Nullable<bool> result = ofd.ShowDialog();
            if (result != true) return "";
            return ofd.FileName;
        }

        private string outputFile(string str)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = str;
            Nullable<bool> result = sfd.ShowDialog();
            if (result != true) return "";
            return sfd.FileName;
        }

        private string fileType(Process process, string filename)
        {
            process.StartInfo.Arguments = "-k " + filename;
            process.Start();
            StreamReader reader = process.StandardOutput;
            string output = reader.ReadToEnd();
            process.WaitForExit();
            process.Close();
            return output;
        }

        private string fileHeader(Process process, string filename)
        {
            process.StartInfo.Arguments = "-h " + filename;
            process.Start();
            StreamReader reader = process.StandardOutput;
            string output = reader.ReadToEnd();
            process.WaitForExit();
            process.Close();
            return output;
        }

        private string parseVarName(string header)
        {
            string subs = header.Substring(0, header.IndexOf("(time, latitude, longitude)"));
            return subs.Substring(subs.LastIndexOf(" ") + 1);
        }

        private int parseDim(string header, string var)
        {
            int i = header.IndexOf(var);
            string subs = header.Substring(i);
            return Int32.Parse(Regex.Match(subs, @"\d+").Value);
        }

        private double[] parseCoordinate(Process process, string filename, string var, int dim, int header_len)
        {
            process.StartInfo.Arguments = "-v " + var + " " + filename;
            process.Start();
            StreamReader reader = process.StandardOutput;
            string output = reader.ReadToEnd();
            process.WaitForExit();
            process.Close();

            string subs = output.Substring(header_len);
            double[] result = new double[dim];
            for (int i = 0; i < dim; i++)
            {
                Match match = Regex.Match(subs, @"\b[0-9]*\.*[0-9]+\b");
                result[i] = Double.Parse(match.Value);
                subs = subs.Substring(match.Index + match.Length);
            }
            return result;
        }

        private double?[,,] parseData(Process process, string filename, string varName, int dim1, int dim2, int dim3, int header_len)
        {
            process.StartInfo.Arguments = "-l 4096 -v " + varName + " " + filename;
            process.Start();
            StreamReader reader = process.StandardOutput;
            string output = reader.ReadToEnd();
            process.WaitForExit();
            process.Close();

            string subs = output.Substring(output.LastIndexOf("="));
            StringReader sr = new StringReader(subs);
            sr.ReadLine();
            double?[,,] result = new double?[dim1, dim2, dim3];
            for (int i = 0; i < dim1; i++)
            {
                for (int j = 0; j < dim2; j++)
                {
                    string line = sr.ReadLine();
                    for (int k = 0; k < dim3; k++)
                    {
                        line = line.Trim();
                        if (line.StartsWith("NaN")) result[i, j, k] = null;
                        else
                        {
                            Match match = Regex.Match(line, @"\b[0-9]*\.*[0-9]+\b");
                            result[i, j, k] = Double.Parse(match.Value);
                        }
                        line = line.Substring(line.IndexOf(",") + 1);
                    }
                }
            }
            return result;
        }

        private void importNC()
        {
            // Get variable name
            varName = parseVarName(fh);

            // Get coordinates dimensions
            int time_dim = parseDim(fh, "time");
            int latitude_dim = parseDim(fh, "latitude");
            int longitude_dim = parseDim(fh, "longitude");

            // Parse data
            time = parseCoordinate(process, importFilename, "time", time_dim, fh.Length);
            latitude = parseCoordinate(process, importFilename, "latitude", latitude_dim, fh.Length);
            longitude = parseCoordinate(process, importFilename, "longitude", longitude_dim, fh.Length);
            data = parseData(process, importFilename, varName, time_dim, latitude_dim, longitude_dim, fh.Length);
            manualEvent_import.Set();
        }

        private void disableBTNs()
        {
            importBTN.IsEnabled = false;
            exportCSV_BTN.IsEnabled = false;
            exportTXT_BTN.IsEnabled = false;
        }

        private void enableBTNs()
        {
            importBTN.IsEnabled = true;
            exportCSV_BTN.IsEnabled = true;
            exportTXT_BTN.IsEnabled = true;
        }

        private void import_clicked(object sender, RoutedEventArgs e)
        {
            // File selection dialog
            string filename = inputFile();
            if (filename == "") return;
            fileTB.Text = filename;
            importFilename = filename;

            // ncdump process
            process.StartInfo.FileName = @"C:\Program Files\netCDF 4.6.0\bin\ncdump.exe";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;

            ft = fileType(process, importFilename); // Read file type
            fh = fileHeader(process, importFilename); // Read file header
            kindTB.Text = ft;
            headerTB.Text = fh;

            // Import nc in separate thread
            disableBTNs();
            statusTB.Text = "Importando, por favor aguarde.";
            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle, null);
            Thread thread = new Thread(new ThreadStart(importNC));
            thread.Start();
            manualEvent_import.WaitOne();
            statusTB.Text = "Pronto!";
            enableBTNs();
        }

        private int getColNum(double lenTotal, double lenLast)
        {
            int minDiv = (int)Math.Ceiling(lenTotal / 1048576);
            while (lenLast % minDiv != 0) minDiv++;
            return minDiv;
        }

        private void writeToFileStream(string str, FileStream fs)
        {
            byte[] b = Encoding.ASCII.GetBytes(str);
            fs.Write(b, 0, b.Length);
        }
        
        private void writeCSV()
        {
            FileStream fs = File.Open(exportFilename, FileMode.Create, FileAccess.Write, FileShare.None);

            // Write separator
            writeToFileStream("sep =,\n", fs);

            // Write column names
            int N = getColNum(time.Length * latitude.Length * longitude.Length, longitude.Length);
            for (int n = 0; n < N; n++)
            {
                writeToFileStream("Time,Latitude,Longitude," + varName, fs);
                if (n+1 == N) writeToFileStream("\n", fs);
                else writeToFileStream(",,", fs);
            }

            // Write data
            for (int i = 0; i < time.Length; i++)
            {
                for (int j = 0; j < latitude.Length; j++)
                {
                    for (int k = 0; k < longitude.Length; k++)
                    {
                        string str = String.Format("{0:G},{1:G},{2:G},{3:G}", time[i], latitude[j], longitude[k], data[i,j,k]);
                        writeToFileStream(str, fs);
                        if ((k+1) % N == 0) writeToFileStream("\n", fs);
                        else writeToFileStream(",,", fs);
                    }
                }
            }
            fs.Close();
            manualEvent_export.Set();
        }

        private void writeTXT()
        {
            FileStream fs = File.Open(exportFilename, FileMode.Create, FileAccess.Write, FileShare.None);
            writeToFileStream("Time;Latitude;Longitude;" + varName + "\n", fs);
            for (int i = 0; i < time.Length; i++)
            {
                for (int j = 0; j < latitude.Length; j++)
                {
                    for (int k = 0; k < longitude.Length; k++)
                    {
                        string str = String.Format("{0:G};{1:G};{2:G};{3:G}\n", time[i], latitude[j], longitude[k], data[i, j, k]);
                        writeToFileStream(str, fs);
                    }
                }
            }
            fs.Close();
            manualEvent_export.Set();
        }

        private void exportCSV_clicked(object sender, RoutedEventArgs e)
        {
            // File selection dialog
            string filename = outputFile("CSV | *.csv");
            if (filename == "") return;
            exportFilename = filename;

            // Write to CSV in separate thread
            disableBTNs();
            statusTB.Text = "Exportando, por favor aguarde.";
            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle, null);
            Thread thread = new Thread(new ThreadStart(writeCSV));
            thread.Start();
            manualEvent_export.WaitOne();
            statusTB.Text = "Pronto!";
            enableBTNs();
        }

        private void exportTXT_clicked(object sender, RoutedEventArgs e)
        {
            // File selection dialog
            string filename = outputFile("TXT | *.txt");
            if (filename == "") return;
            exportFilename = filename;

            // Write to TXT in separate thread
            disableBTNs();
            statusTB.Text = "Exportando, por favor aguarde.";
            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle, null);
            Thread thread = new Thread(new ThreadStart(writeTXT));
            thread.Start();
            manualEvent_export.WaitOne();
            statusTB.Text = "Pronto!";
            enableBTNs();
        }
    }
}
