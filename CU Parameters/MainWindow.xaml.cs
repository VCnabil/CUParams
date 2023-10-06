using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using CANUtils;
using Microsoft.Win32;

namespace CU_Parameters
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        string? savefn = null;
        CANHandler? CAN = null;
        SynchronizationContext? sctx;

        public string[] CANChannels => CANHandler.GetCANChannels();

        public string TitleText => $"Control Unit Parameters{(savefn != null ? " - " + savefn : "")}";

        public ObservableCollection<Parameter> Parameters { get; private set; } = new ObservableCollection<Parameter>();

        string[]? paramnames = null;

        public MainWindow()
        {
            InitializeComponent();

            sctx = SynchronizationContext.Current;

            DataContext = this;

            Closing += MainWindow_Closing;

            if (CANChannels.Length > 0)
                comboCAN.SelectedIndex = 0;
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (CAN != null)
                CAN.Close();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void ImportFromCode_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();

            ofd.Filter = "Parameter Code File|parameters.cpp|All Files (*.*)|*.*";

            if (ofd.ShowDialog() == false)
                return;

            paramnames = ImportParametersfromCode(ofd.FileName);

            UpdateParameterNames();

            SaveFileDialog sfd = new SaveFileDialog();

            sfd.Filter = "Parameter Names File (*.paramnames)|*.paramnames|All Files (*.*)|*.*";

            if (sfd.ShowDialog() == false)
                return;

            File.WriteAllText(sfd.FileName, JsonSerializer.Serialize(paramnames, new JsonSerializerOptions() { WriteIndented = true }));
        }

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();

            ofd.Filter = "Parameter Names File (*.paramnames)|*.paramnames|All Files (*.*)|*.*";

            if (ofd.ShowDialog() == false)
                return;

            paramnames = JsonSerializer.Deserialize<string[]>(File.ReadAllText(ofd.FileName));

            UpdateParameterNames();
        }

        void UpdateParameterNames()
        {
            int i;

            if (paramnames == null)
                return;

            for (i = 0; i < paramnames.Length; i++)
            {
                if (Parameters.Count > i)
                    Parameters[i].Name = paramnames[i];
                else
                    Parameters.Add(new Parameter(paramnames[i], 0, true));
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();

            ofd.Filter = "Parameter File (*.params)|*.params|All Files (*.*)|*.*";

            if (ofd.ShowDialog() == false)
                return;

            var ps = JsonSerializer.Deserialize<Parameter[]>(File.ReadAllText(ofd.FileName));

            savefn = ofd.FileName;

            OnPropertyChanged(nameof(TitleText));

            Parameters.Clear();
            paramnames = null;

            if (ps == null)
                return;

            foreach (var p in ps)
                Parameters.Add(p);
            paramnames = Parameters.Select(a => a.Name).ToArray();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (savefn == null)
                SaveAs_Click(sender, e);
            else
                SaveParameters(savefn);
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();

            sfd.Filter = "Parameter File (*.params)|*.params|All Files (*.*)|*.*";

            if (savefn != null)
                sfd.FileName = System.IO.Path.GetFileName(savefn);

            if (sfd.ShowDialog() == false)
                return;

            SaveParameters(sfd.FileName);
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            Parameters.Clear();
            paramnames = null;
        }

        void SaveParameters(string filename)
        {
            File.WriteAllText(filename, JsonSerializer.Serialize(Parameters, new JsonSerializerOptions() { WriteIndented = true }));

            savefn = filename;

            OnPropertyChanged(nameof(TitleText));
        }

        private void btnRefreshCANList_Click(object sender, RoutedEventArgs e)
        {
            OnPropertyChanged(nameof(CANChannels));
        }

        void SetButtonState(bool enable)
        {
            btnRead.IsEnabled = enable;
            btnWrite.IsEnabled = enable;
            btnWriteAll.IsEnabled = enable;
            menuFile.IsEnabled = enable;
            menuOptions.IsEnabled = enable;
            chkWriteEEPROM.IsEnabled = enable;
        }

        private void btnRead_Click(object sender, RoutedEventArgs e)
        {
            if (CAN != null)
                CAN.Close();

            Parameters.Clear();
            firstindex = -1;

            CAN = new CANHandler((string)comboCAN.SelectedItem, CANCB);

            SetButtonState(false);
        }

        private void btnWrite_Click(object sender, RoutedEventArgs e)
        {
           if (CAN != null)
                CAN.Close();

            CAN = new CANHandler((string)comboCAN.SelectedItem, CANCBWrite);
            
            SetButtonState(false);

            WriteDirtyParams();

            firstindex = -1;
            writeiterationcount = 0;
            paramwritewait = false;
        }

        void WriteDirtyParams()
        {
            int i = 0;

            foreach (var p in Parameters)
            {
                if (p.IsDirty)
                {
                    WriteParam(i, p, chkWriteEEPROM.IsChecked == true);

                    Thread.Sleep(50);
                }

                i++;
            }
        }

        void WriteParam(int index, Parameter p, bool writeeeprom)
        {
            byte[] data = new byte[8];

            data[0] = (byte)'V';
            data[1] = (byte)'C';
            Array.Copy(BitConverter.GetBytes((ushort)index), 0, data, 2, 2);
            Array.Copy(BitConverter.GetBytes(p.RawValue), 0, data, 4, 2);

            if (writeeeprom)
                data[3] |= 0x80;

            CAN?.SendCAN(0x18fffcfe, data);
        }

        bool paramwritewait = false;
        int writeiterationcount = 0;

        void CANCBWrite(uint id, byte[] data)
        {
            int index, maxindex;
            ushort[] vals = new ushort[3];

            if (id != 0x18ff6029 || paramwritewait)
                return;

            index = (data[0] | (data[1] << 8)) & 0x3fff;
            maxindex = data[1] >> 6;
            if (maxindex > 0)
                maxindex += index - 1;
            else
                maxindex = -1;
            if (firstindex == -1)
                firstindex = index;
            else if (index >= firstindex && (index - firstindex) < 3)
            {
                if (writeiterationcount < 5 && Parameters.Any(a => a.IsDirty))
                {
                    paramwritewait = true;

                    sctx?.Post((_) =>
                    {
                        WriteDirtyParams();

                        paramwritewait = false;
                    }, null);

                    firstindex = -1;
                    writeiterationcount++;
                }
            }

            vals[0] = (ushort)(data[2] | (data[3] << 8));
            vals[1] = (ushort)(data[4] | (data[5] << 8));
            vals[2] = (ushort)(data[6] | (data[7] << 8));

            sctx?.Post((_) =>
            {
                UpdateParameters(index, maxindex, vals, true);

                if (!Parameters.Any(a => a.IsDirty))
                    WriteDone(false);
                else if (writeiterationcount == 5)
                    WriteDone(true);
            }, null);
        }

        void WriteDone(bool error)
        {
            CAN?.Close();

            SetButtonState(true);

            if (error)
                MessageBox.Show("Error writing parameters", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        void UpdateParameter(int index, ushort val, bool checkdirty = false)
        {
            if (checkdirty && Parameters[index].IsDirty && val != Parameters[index].RawValue)
                return;

            if (paramnames != null && paramnames.Length > index)
                Parameters[index].SetValue(paramnames[index], val);
            else
                Parameters[index].SetValue(val);
        }

        void UpdateParameters(int index, int maxindex, ushort[] vals, bool checkdirty)
        {
            int i;

            while (Parameters.Count < (index + 3) && (maxindex == -1 || Parameters.Count < (maxindex + 1)))
                Parameters.Add(new Parameter(paramnames != null && paramnames.Length > Parameters.Count ? paramnames[Parameters.Count] : $"Parameter {Parameters.Count}", 0, true));

            for (i = 0; i < 3; i++)
            {
                UpdateParameter(index++, vals[i], checkdirty);
                if (maxindex != -1 && index > maxindex)
                    index = 0;
            }
        }

        int firstindex = -1;

        void CANCB(uint id, byte[] data)
        {
            int index, maxindex;
            ushort[] vals = new ushort[3];

            if (id != 0x18ff6029)
                return;

            index = (data[0] | (data[1] << 8)) & 0x3fff;
            maxindex = data[1] >> 6;
            if (maxindex > 0)
                maxindex += index - 1;
            else
                maxindex = -1;
            if (firstindex == -1)
                firstindex = index;
            else if (index >= firstindex && (index - firstindex) < 3)
            {
                sctx?.Post((_) =>
                {
                    CAN?.Close();

                    SetButtonState(true);
                }, null);

                return;
            }

            vals[0] = (ushort)(data[2] | (data[3] << 8));
            vals[1] = (ushort)(data[4] | (data[5] << 8));
            vals[2] = (ushort)(data[6] | (data[7] << 8));

            sctx?.Post((_) => UpdateParameters(index, maxindex, vals, false), null);
        }

        string[] ImportParametersfromCode(string filename)
        {
            List<string> ret = new List<string>();
            StreamReader sr = new StreamReader(filename);
            string? line, v;
            string[] sp;
            int i;

            while (!sr.EndOfStream)
            {
                line = sr.ReadLine();
                if (line == null)
                    continue;

                sp = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (sp.Length < 2)
                    continue;

                if (sp[0] == "EParam")
                {
                    v = "";

                    for (i = 0; i < sp[1].Length; i++)
                    {
                        if (sp[1][i] == '(' || sp[1][i] == '=')
                            break;

                        v += sp[1][i];
                    }

                    ret.Add(v);
                }
            }

            return ret.ToArray();
        }

        private void btnWriteAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in Parameters)
                p.SetDirty();

            btnWrite_Click(sender, e);
        }
    }

    public class Parameter : INotifyPropertyChanged
    {
        public string Name
        {
            get => name;
            set
            {
                name = value;

                OnPropertyChanged();
            }
        }
        string name;
        public ushort RawValue { get; set; }
        [JsonIgnore]
        public string Value
        {
            get => DefaultValue ? "Unset" : RawValue.ToString();
            set
            {
                ushort val;

                if (!ushort.TryParse(value, out val))
                    return;

                RawValue = val;

                IsDirty = true;
                OnPropertyChanged(nameof(IsDirty));
            }
        }
        [JsonIgnore]
        public bool DefaultValue { get; private set; }
        [JsonIgnore]
        public bool IsDirty { get; private set; }

        public Parameter()
        {
            name = "";
            RawValue = 0;
            DefaultValue = false;
        }

        public Parameter(string name, ushort value, bool defaultval = false)
        {
            this.name = name;
            RawValue = value;
            DefaultValue = defaultval;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetValue(ushort value)
        {
            RawValue = value;
            DefaultValue = false;

            OnPropertyChanged(nameof(Value));

            IsDirty = false;
            OnPropertyChanged(nameof(IsDirty));
        }

        public void SetValue(string name, ushort value)
        {
            Name = name;
            RawValue = value;
            DefaultValue = false;

            OnPropertyChanged(nameof(Value));

            IsDirty = false;
            OnPropertyChanged(nameof(IsDirty));
        }

        public void SetDirty()
        {
            IsDirty = true;
            OnPropertyChanged(nameof(IsDirty));
        }

        public void SetClean()
        {
            IsDirty = false;
            OnPropertyChanged(nameof(IsDirty));
        }

        public void SetDefault()
        {
            RawValue = 0;
            DefaultValue = true;
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class DirtyStateToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return Brushes.Transparent;

            var dirty = (bool)value;

            if (dirty)
                return Brushes.LightBlue;
            else
                return Brushes.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
