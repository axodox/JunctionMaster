using System.Linq;
using System.Windows;
using JunctionMaster.Properties;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using Junctions;
using System.IO;
using System.Reflection;
using System;

namespace JunctionMaster
{
    public partial class MainWindow : Window
    {
        DirectoryMover DM;
        readonly string CD = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        public MainWindow()
        {
            InitializeComponent();
            DM = new DirectoryMover();
            DM.Completed += DM_Completed;
            LoadSettings();
            RefreshFiles();
            LBDirectories.ItemsSource = DM.Directories;
        }

        void DM_Completed(object sender, System.EventArgs e)
        {
            MessageBox.Show("Move complete.");
            RefreshFiles();
        }

        public void LoadSettings()
        {
            TBDestinationVolumeOnTargetOS.Text = Settings.Default.DestinationVolumeOnTargetOS;
            TBSourceVolumeOnTargetOS.Text = Settings.Default.SourceVolumeOnTargetOS;
            if (!string.IsNullOrEmpty(Settings.Default.DestinationPath))
            {
                TBDestinationVolumeOnHostOS.Text = Settings.Default.DestinationPath[0].ToString();
                TBDestinationPath.Text = DM.DestinationPath = Settings.Default.DestinationPath;
            }

            string[] dirs = Settings.Default.SourceDirectories.Split(new char[] {'|'},System.StringSplitOptions.RemoveEmptyEntries);
            foreach (string dir in dirs) AddDirectory(dir);
        }

        private void RefreshFiles()
        {
            string[] recoveryFiles = Directory.GetFiles(CD, "RecoverJunctions_*.bat");
            LBRecovery.ItemsSource = recoveryFiles;

            string[] finalizerFiles = Directory.GetFiles(CD, "FinalizeJunctions_*.bat");
            LBFinalize.ItemsSource = finalizerFiles;
        }

        public bool AddDirectory(string dir)
        {
            if (TBSourceVolumeOnHostOS.Text == "?")
            {
                TBSourceVolumeOnHostOS.Text = dir[0].ToString();
            }
            if (TBSourceVolumeOnHostOS.Text == dir[0].ToString())
            {
                if (Directory.Exists(dir)) DM.Directories.Add(dir);
                return true;
            }
            else return false;
        }

        public void SaveSettings()
        {
            Settings.Default.DestinationVolumeOnTargetOS = TBDestinationVolumeOnTargetOS.Text;
            Settings.Default.SourceVolumeOnTargetOS = TBSourceVolumeOnTargetOS.Text;
            Settings.Default.SourceDirectories = string.Join("|", DM.Directories.ToArray());
            Settings.Default.DestinationPath = DM.DestinationPath;
            Settings.Default.Save();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveSettings();
        }

        private void BAddDirectory_Click(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog FBD = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (FBD.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    if (FBD.SelectedPath.Length <= 3)
                        MessageBox.Show("You cannot move the root of the drive!");
                    else
                        if (!AddDirectory(FBD.SelectedPath)) MessageBox.Show("All directories must be from the same drive!");
                }
            }
        }

        private void LBDirectories_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            BRemoveDirectory.IsEnabled = LBDirectories.SelectedItem != null;
        }

        private void BRemoveDirectory_Click(object sender, RoutedEventArgs e)
        {
            DM.Directories.Remove((string)LBDirectories.SelectedItem);
        }

        
        private void BStart_Click(object sender, RoutedEventArgs e)
        {
            Move(false);
        }

        private void BBrowseDestination_Click(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog FBD = new System.Windows.Forms.FolderBrowserDialog())
            {
                if (FBD.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    DM.DestinationPath = (FBD.SelectedPath.Last() == '\\' ? FBD.SelectedPath : FBD.SelectedPath + "\\");
                    TBDestinationPath.Text = FBD.SelectedPath;
                    TBDestinationVolumeOnHostOS.Text = FBD.SelectedPath[0].ToString();
                }
            }
        }

        private void BClearDirectories_Click(object sender, RoutedEventArgs e)
        {
            DM.Directories.Clear();
            TBSourceVolumeOnHostOS.Text = "?";
        }

        private void Move(bool execute)
        {
            DM.DestinationVolumeOnTargetOS = TBDestinationVolumeOnTargetOS.Text[0];
            DM.SourceVolumeOnTargetOS = TBSourceVolumeOnTargetOS.Text[0];
            DM.Move(CD, execute);
        }

        private void BExecute_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Moving directories with wrong parameters can cause the PC unable to boot. Are you sure?", "Moving directories", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                Move(true);
        }

        private void BRecover_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("You are going to restore the original state of the folders. Do you proceed?", "Recovering junctions", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                BatchRunner.RunFile((string)LBRecovery.SelectedItem, RecoveryComplete);
        }

        private void RecoveryComplete(object sender, EventArgs e)
        {
            MessageBox.Show("Recovery complete");
        }

        private void LBRecovery_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            BRecover.IsEnabled = LBRecovery.SelectedItem != null;
        }

        private void BFinalize_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("You are going to remove the original state of the folders. Do you proceed?", "Deleting backup", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                BatchRunner.RunFile((string)LBFinalize.SelectedItem, FinalizeComplete);
        }

        private void FinalizeComplete(object sender, EventArgs e)
        {
            MessageBox.Show("Finalize complete");
        }

        private void LBFinalize_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            BFinalize.IsEnabled = LBFinalize.SelectedItem != null;
        }

        private void BLoadDirectories_Click(object sender, RoutedEventArgs e)
        {
            using(System.Windows.Forms.OpenFileDialog OFD=new System.Windows.Forms.OpenFileDialog())
            using(System.Windows.Forms.FolderBrowserDialog OBD=new System.Windows.Forms.FolderBrowserDialog())
            {
                OFD.Title = "Select folder list";
                OFD.Filter="Text Files (*.txt)|*.txt";
                OBD.Description = "Select destination directory";
                if (OFD.ShowDialog() == System.Windows.Forms.DialogResult.OK && OBD.ShowDialog()==System.Windows.Forms.DialogResult.OK)
                {
                    DM.Directories.Clear();
                    TBSourceVolumeOnHostOS.Text = "?";
                    if (OBD.SelectedPath.Last() != '\\') OBD.SelectedPath += "\\";
                    string[] lines = File.ReadAllLines(OFD.FileName);
                    foreach (string l in lines)
                        AddDirectory(OBD.SelectedPath + l);
                }
            }
        }
    }
}
