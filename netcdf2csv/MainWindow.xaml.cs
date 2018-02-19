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
    public partial class MainWindow : Window
    {
        //=========================//
        //=== PRIVATE VARIABLES ===//
        //=========================//

        private double[] latitude, longitude, time;
        private TextBox fileTB, kindTB, headerTB;
        private Button importBTN, exportTXT_BTN;
        private TextBlock statusTB;
        private string importFilename, exportFilename, tempFilename = Path.GetTempFileName(), ft, fh, varName;
        private static ManualResetEvent manualEvent = new ManualResetEvent(false);
        private Process process = new Process();
        private const int READ_NC = 0, WRITE_CSV = 1, WRITE_TXT = 2;

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
            // Call ncdump
            process.StartInfo.Arguments = "-v " + var + " " + filename;
            process.Start();
            StreamReader reader = process.StandardOutput;
            string output = reader.ReadToEnd();
            process.WaitForExit();
            process.Close();

            // Parse standard output
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

        private void saveDataToTemp(Process process, string filename, string varName)
        {
            process.StartInfo.Arguments = "-l 4096 -v " + varName + " " + filename;
            process.Start();
            StreamReader reader = process.StandardOutput;
            string output = reader.ReadToEnd();
            process.WaitForExit();
            process.Close();

            FileStream fs = File.Open(tempFilename, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            writeToFileStream(output, fs);
        }

        private void disableBTNs()
        {
            importBTN.IsEnabled = false;
            exportTXT_BTN.IsEnabled = false;
        }

        private void enableBTNs()
        {
            importBTN.IsEnabled = true;
            exportTXT_BTN.IsEnabled = true;
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

        private void readNC()
        {
            // Get variable name
            varName = parseVarName(fh);

            // Get coordinates dimensions
            int time_dim = parseDim(fh, "time");
            int latitude_dim = parseDim(fh, "latitude");
            int longitude_dim = parseDim(fh, "longitude");

            // Parse dimensions
            time = parseCoordinate(process, importFilename, "time", time_dim, fh.Length);
            latitude = parseCoordinate(process, importFilename, "latitude", latitude_dim, fh.Length);
            longitude = parseCoordinate(process, importFilename, "longitude", longitude_dim, fh.Length);

            // Save data to temp file
            saveDataToTemp(process, importFilename, varName);
            manualEvent.Set();
        }

        private void writeTXT()
        {
            // Read from temp file
            string data_str = File.ReadAllText(tempFilename);
            data_str = data_str.Substring(data_str.LastIndexOf("=")); // trim start
            StringReader sr = new StringReader(data_str);

            // Export to file
            FileStream fs = File.Open(exportFilename, FileMode.Create, FileAccess.Write, FileShare.None);
            writeToFileStream("Time;Latitude;Longitude;" + varName + "\n", fs);
            sr.ReadLine(); // first line is trashed
            for (int i = 0; i < time.Length; i++)
            {
                for (int j = 0; j < latitude.Length; j++)
                {
                    string line = sr.ReadLine();
                    for (int k = 0; k < longitude.Length; k++)
                    {
                        line = line.Trim();
                        double? data; 
                        if (line.StartsWith("NaN")) data = null;
                        else
                        {
                            Match match = Regex.Match(line, @"\b[0-9]*\.*[0-9]+\b");
                            data = Double.Parse(match.Value);
                        }
                        line = line.Substring(line.IndexOf(",") + 1);
                        string str = String.Format("{0:G};{1:G};{2:G};{3:G}\n", time[i], latitude[j], longitude[k], data);
                        writeToFileStream(str, fs);
                    }
                }
            }
            fs.Close();
            manualEvent.Set();
        }

        private void execute_thread(int action)
        {
            // Update UI
            disableBTNs();
            if (action == READ_NC) statusTB.Text = "Importando, por favor aguarde.";
            else statusTB.Text = "Exportando, por favor aguarde.";
            Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.ContextIdle, null);

            // Create separate thread
            Thread thread;
            switch (action)
            {
                default:
                case READ_NC:
                    thread = new Thread(new ThreadStart(readNC)); 
                    break;
                case WRITE_TXT:
                    thread = new Thread(new ThreadStart(writeTXT));
                    break;
                    /*
                case WRITE_CSV:
                    thread = new Thread(new ThreadStart(writeCSV));
                    break;
                    */
            }
            manualEvent.Reset();
            thread.Start();
            manualEvent.WaitOne();

            // Update UI
            statusTB.Text = "Pronto!";
            enableBTNs();
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
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;

            ft = fileType(process, importFilename); // Read file type
            fh = fileHeader(process, importFilename); // Read file header
            kindTB.Text = ft;
            headerTB.Text = fh;

            execute_thread(READ_NC);
        }

        private void exportCSV_clicked(object sender, RoutedEventArgs e)
        {
            string filename = outputFile("CSV | *.csv");
            if (filename == "") return;
            exportFilename = filename;
            execute_thread(WRITE_CSV);
        }

        private void exportTXT_clicked(object sender, RoutedEventArgs e)
        {
            string filename = outputFile("TXT | *.txt");
            if (filename == "") return;
            exportFilename = filename;
            execute_thread(WRITE_TXT);
        }

        /*
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
            manualEvent.Set();
        }
        */
    }
}
