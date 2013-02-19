#region Namespaces

using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Expression.Encoder.Devices;
using Microsoft.Expression.Encoder.ScreenCapture;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using Microsoft.WindowsAzure.MediaServices.Client;
using ProtoScreenCaptureApp.Helpers;

#endregion

namespace ProtoScreenCaptureApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DataViewModel _dataViewModel;

        public MainWindow()
        {

            
            InitializeComponent();
            _dataViewModel = new DataViewModel();
            DataContext = _dataViewModel;
           

            //// Sets default capture window dimensions
            //int x = 300, y = 300;
            //int w = 200, h = 200;

            //// Set rectangle
            //Canvas.SetLeft(CaptureRect, 0);
            //Canvas.SetTop(CaptureRect,0);

            //CaptureRect.Width = SystemParameters.PrimaryScreenWidth;
            //CaptureRect.Height= SystemParameters.PrimaryScreenHeight;
        }

        private void OnCaptureStart(object sender, RoutedEventArgs e)
        {
            btnCapture.IsEnabled = false;
            btnStop.IsEnabled = true;
            _dataViewModel.StartCapturing();
        }

        private void OnCaptureStop(object sender, RoutedEventArgs e)
        {
            _dataViewModel.StopCapturing();
            btnCapture.IsEnabled = true;
            btnStop.IsEnabled = false;
        }

        private void OnNavigateClick(object sender, RoutedEventArgs e)
        {
            var diaolog = new WPFFolderBrowser.WPFFolderBrowserDialog { InitialDirectory = _dataViewModel.Path };
            var result = diaolog.ShowDialog();
            if (result != null && result.Value)
                _dataViewModel.Path = diaolog.FileName;
        }

        private void OnMouseLeftButtonDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var control = sender as ListBox;
            if (control != null && control.SelectedItems.Count > 0)
            {
                var video = (control.SelectedItems[0] as VideoViewModel);
                if (video != null)
                    mediaPreview.Source = new Uri(video.LocalPath, UriKind.Absolute);    
            }
        }

        private void OnSendToAzureClick(object sender, RoutedEventArgs e)
        {
            var video = listBoxVideos.Items.GetItemAt(listBoxVideos.SelectedIndex) as VideoViewModel;
            if (video != null)
            {
                Dispatcher.CurrentDispatcher.InvokeAsync(() =>
                                                             {
                                                                 var asset = MediaServiceHelper.CreateAssetAndUploadSingleFile(AssetCreationOptions.None, video.LocalPath, x => { video.CurrentStatus = x; });
                                                                 MediaServiceHelper.CreateEncodingJob(asset, x => video.CurrentStatus = x, x =>
                                                                                                                                               {
                                                                                                                                                   video.Url = x??string.Empty;
                                                                                                                                                   Clipboard.SetData(DataFormats.Text, x??string.Empty);
                                                                                                                                                   MessageBox.Show("Link was copied to buffer");
                                                                                                                                               });
                                                             });
            }
        }
    }

    public class DataViewModel : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged
        
        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        #region Privates
        private readonly ScreenCaptureJob _job;
        #endregion

        #region Properties

        public ObservableCollection<VideoViewModel> Videos { get; set; }
        
        private ObservableCollection<string> _screenCapturesPathes;
        public ObservableCollection<string> ScreenCapturesPathes { 
            get { return _screenCapturesPathes ?? (_screenCapturesPathes = new ObservableCollection<string>()); }
            set
            {
                if (_screenCapturesPathes == null)
                    _screenCapturesPathes = new ObservableCollection<string>();
                _screenCapturesPathes = value; 
                OnPropertyChanged("ScreenCapturesPathes");
            }
        }

        private ObservableCollection<EncoderDevice> _audioDevices;
        public ObservableCollection<EncoderDevice> AudioDevices
        {
            get { return _audioDevices; }
            set { _audioDevices = value; OnPropertyChanged("AudioDevices"); }
        }

        private string _path;
        public String Path
        {
            get { return _path; }
            set { _path = value; OnPropertyChanged("Path"); }
        }

        private bool _isCurrentylRecord;
        public bool IsCurrentylRecord {
            get { return _job.Status == RecordStatus.Running; }
            set { OnPropertyChanged("IsCurrentylRecord"); }
        }
        #endregion

        #region Constructor
        public DataViewModel()
        {
            _job = new ScreenCaptureJob();
            Path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            AudioDevices = new ObservableCollection<EncoderDevice>(EncoderDevices.FindDevices(EncoderDeviceType.Audio));
            Videos = new ObservableCollection<VideoViewModel>();
        }
        #endregion

        public void StartCapturing()
        {
            if (!IsCurrentylRecord)
            {
                //_job.CaptureFollowCursor = true;
               // _job.ShowCountdown = true;
                //_job.ShowFlashingBoundary = true;
                _job.OutputPath = Path;
                _job.AddAudioDeviceSource(EncoderDevices.FindDevices(EncoderDeviceType.Audio).FirstOrDefault(i => i.Category == EncoderDeviceCategory.Capture));
                _job.Start();
            }
        }

        public void StopCapturing()
        {
            if (IsCurrentylRecord)
            {
                Videos.Add(new VideoViewModel
                               {
                                   LocalPath = _job.ScreenCaptureFileName,
                                   CurrentStatus = "local"
                               });
                ScreenCapturesPathes.Add(_job.ScreenCaptureFileName);
                _job.Stop();
            }
        }
    }

    public class VideoViewModel :INotifyPropertyChanged
    {
        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        public string LocalPath
        {
            get { return _localPath; }
            set { _localPath = value; OnPropertyChanged("LocalPath"); }
        }
        private string _localPath;

        public string Url
        {
            get { return _url; }
            set { _url = value; OnPropertyChanged("Url"); }
        }
        private string _url;

        public double Prorgress {
            get { return _progress; }
            set { _progress = value; OnPropertyChanged("Prorgress"); }
        }
        private double _progress;

        public string CurrentStatus
        {
            get { return _currentStatus; }
            set { _currentStatus = value; OnPropertyChanged("CurrentStatus"); }
        }
        private string _currentStatus;

    }
    
}
