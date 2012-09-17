using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.ComponentModel;
using SharedClasses;
using System.Web;

namespace StandaloneDownloader
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		ObservableCollection<DownloadingItem> currentlyDownloadingList = new ObservableCollection<DownloadingItem>();

		public MainWindow()
		{
			InitializeComponent();
		}

		private void Window_Loaded(object sender, RoutedEventArgs e)
		{
			currentlyDownloadingList.Add(new DownloadingItem("https://fjh.dyndns.org/downloadownapps.php?relativepath=AutoUpdater/Setup_AutoUpdater_1_0_0_88.exe", @"c:\francois\tmp1.exe", true, true));
			currentlyDownloadingList.Add(new DownloadingItem("https://fjh.dyndns.org/downloadownapps.php?relativepath=PublishOwnApps/Setup_PublishOwnApps_1_0_0_151.exe", @"c:\francois\tmp2.exe", true, true));
			listboxDownloadList.ItemsSource = currentlyDownloadingList;
		}

		private DownloadingItem GetDownloadingItemFromPossibleFrameworkElement(object possibleButton)
		{
			FrameworkElement fwe = possibleButton as FrameworkElement;
			if (fwe == null) return null;
			var downloadingItem = fwe.DataContext as DownloadingItem;
			return downloadingItem;
		}

		private void buttonRemoveFromList_Click(object sender, RoutedEventArgs e)
		{
			var item = GetDownloadingItemFromPossibleFrameworkElement(sender);
			if (item == null) return;
			if (currentlyDownloadingList.Contains(item))
			{
				currentlyDownloadingList.Remove(item);
				if (currentlyDownloadingList.Count == 0)
					this.Close();//Exit application
			}
		}

		private void buttonCancelButtonVisibility_Click(object sender, RoutedEventArgs e)
		{
			var item = GetDownloadingItemFromPossibleFrameworkElement(sender);
			if (item == null) return;
			item.CancelDownloadingAsync();
		}
	}

	public class DownloadingItem : INotifyPropertyChanged
	{
		public const string cThisAppName = "StandaloneDownloader";

		private static List<DownloadingItem> DownloadsInProgress = new List<DownloadingItem>();
		private static Queue<DownloadingItem> DownloadsQueued = new Queue<DownloadingItem>();
		private static Dictionary<DownloadingItem, string> UnssuccessfulListOfFilepathsAndItem = new Dictionary<DownloadingItem, string>();//Keeping track of items to remove them (and delete file) once they are successful

		public string DownloadUrl { get; private set; }
		public string LocalPath { get; private set; }

		private string  _currentprogressmessage;
		public string CurrentProgressMessage { get { return _currentprogressmessage; } set { _currentprogressmessage = value; OnPropertyChanged("CurrentProgressMessage"); } }

		private int _currentprogresspercentage;
		public int CurrentProgressPercentage { get { return _currentprogresspercentage; } set { _currentprogresspercentage = value; OnPropertyChanged("CurrentProgressPercentage"); } }

		private Visibility _removefromlistbuttonvisibility;
		public Visibility RemoveFromListButtonVisibility
		{
			get { return _removefromlistbuttonvisibility; }
			set { _removefromlistbuttonvisibility = value; OnPropertyChanged("RemoveFromListButtonVisibility"); }
		}

		private Visibility _cancelbuttonvisibility;
		public Visibility CancelButtonVisibility
		{
			get { return _cancelbuttonvisibility; }
			set { _cancelbuttonvisibility = value; OnPropertyChanged("CancelButtonVisibility"); }
		}

		private bool MustCancel = false;
		public bool SuccessfullyUploaded = false;

		public DownloadingItem(string DownloadUrl, string LocalPath, bool AutoStart, bool AllowCancel)
		{
			this.DownloadUrl = DownloadUrl;
			this.LocalPath = LocalPath;
			this.CurrentProgressMessage = "Item queued.";
			this.CancelButtonVisibility = AllowCancel ? Visibility.Visible : Visibility.Collapsed;
			//this.DeleteOnlineFileAndRetryButtonVisibility = Visibility.Collapsed;
			//this.RetryUploadButtonVisibility = Visibility.Collapsed;
			this.RemoveFromListButtonVisibility = Visibility.Collapsed;
			if (AutoStart)
				StartUploading();
			AddToUnssuccesfulList();
		}

		private static object lockobject = new object();
		private static string unsuccessfulUploadsDirpath = Path.GetDirectoryName(SettingsInterop.GetFullFilePathInLocalAppdata("tmp", cThisAppName, "UnsuccessfulUploads"));
		private void AddToUnssuccesfulList()
		{
			if (!UnssuccessfulListOfFilepathsAndItem.ContainsKey(this))
			{
			retrywritefile:
				lock (lockobject)
				{
					try
					{
						string filepathToUnsuccessfulList = Path.Combine(unsuccessfulUploadsDirpath, DateTime.Now.ToString("yyyy_MM_dd HH_mm_ss_fffff"));
						File.WriteAllText(filepathToUnsuccessfulList, this.GetFileLineForApplicationRecovery());
						UnssuccessfulListOfFilepathsAndItem.Add(this, filepathToUnsuccessfulList);
					}
					catch
					{
						Thread.Sleep(500);
						goto retrywritefile;
					}
				}
			}
		}

		private void RemoveFromUnsuccessfulList()
		{
			if (UnssuccessfulListOfFilepathsAndItem.ContainsKey(this))
			{
				string filepath = UnssuccessfulListOfFilepathsAndItem[this];
				UnssuccessfulListOfFilepathsAndItem.Remove(this);
				File.Delete(filepath);
			}
		}

		private void AddSuccessfulDownloadToHistory()
		{
			Logging.LogSuccessToFile(
				string.Format("Successfully downloaded from '{0}' to local file '{1}'",
					this.DownloadUrl, this.LocalPath),
				Logging.ReportingFrequencies.Daily,
				cThisAppName,
				"SuccessHistory");
		}

		public void CancelDownloadingAsync()
		{
			MustCancel = true;
		}

		public string GetFileLineForApplicationRecovery()
		{
			//if (this.DownloadUrl != null)
			return this.DownloadUrl;
			//return string.Join("|", this.DisplayName, this.ProtocolType.ToString(), this.LocalPath, this.FtpUrl, this.AutoOverwriteIfExists, this.FtpUsername, this.FtpPassword);
		}

		Thread uploadingThread;
		private void StartUploading()
		{
			uploadingThread = new Thread((ThreadStart)delegate
			{
				//if (!Path.GetExtension(this.LocalPath).Equals(Path.GetExtension(this.FtpUrl)))
				//    this.CurrentProgressMessage = "Error: cannot download file, file-extension different for LocalPath and FtpUrl, please use FULL url for FtpUrl.";
				//else
				//{
				if (DownloadsInProgress.Count == 0)
				{
					DownloadsInProgress.Add(this);

					try
					{
						string relativePath = GetRelativePathFromFullUrl();
						if (relativePath == null)
							return;

						if (File.Exists(this.LocalPath))
							File.Delete(this.LocalPath);

						this.CurrentProgressMessage = "Download started, please wait...";
						string err;
						bool? downloadresult = PhpDownloadingInterop.PhpDownloadFile(
							relativePath,
							this.LocalPath,
							null,
							out err,
							(progress, bytespersec) =>
							{
								if (progress != this.CurrentProgressPercentage) this.CurrentProgressPercentage = progress != 100 ? progress : 0;
								this.CurrentProgressMessage = string.Format("{0:0.###} kB/s", bytespersec / 1024D);
							},
							delegate { return true; });

						if (downloadresult != true)//Failed
						{
							this.CurrentProgressMessage = "Unable to download: " + err;
							return;//Exit jumps to the finally section
						}

						this.CurrentProgressMessage = "Successfully downloaded.";
						AddSuccessfulDownloadToHistory();
						this.RemoveFromListButtonVisibility = Visibility.Visible;
						if (!this.MustCancel)
						{
							//actionOnStatusMessage("Successfully uploaded.");// + localFilename);
							this.CancelButtonVisibility = Visibility.Collapsed;
							this.SuccessfullyUploaded = true;
							RemoveFromUnsuccessfulList();
						}
					}
					finally
					{
						DownloadsInProgress.Remove(this);//Removes although could have failed, so that next can start
						if (DownloadsQueued.Count > 0)
							DownloadsQueued.Dequeue().StartUploading();
					}
				}
				else
					DownloadsQueued.Enqueue(this);
				//}
			});
			uploadingThread.Start();
		}

		private string GetRelativePathFromFullUrl()
		{
			if (!this.DownloadUrl.Contains('?'))
			{
				UserMessages.ShowWarningMessage("Cannot obtain relative path from download URL, no ? found in url: " + this.DownloadUrl);
				return null;
			}

			int indexOfQuestionMark = this.DownloadUrl.IndexOf('?');
			var parsed = HttpUtility.ParseQueryString(this.DownloadUrl.Substring(indexOfQuestionMark + 1));
			var relativePaths = parsed.GetValues("relativepath");
			if (relativePaths == null || relativePaths.Length == 0)
			{
				UserMessages.ShowWarningMessage("Cannot obtain relative path from download URL, unable to find relativepath value in url:" + this.DownloadUrl);
				return null;
			}
			else if (relativePaths.Length > 1)
			{
				UserMessages.ShowWarningMessage("Cannot obtain relative path, multiple relative path values found in url: " + this.DownloadUrl);
				return null;
			}
			return relativePaths[0];
		}

		public event PropertyChangedEventHandler PropertyChanged = delegate { };
		public void OnPropertyChanged(string propertyName) { PropertyChanged(this, new PropertyChangedEventArgs(propertyName)); }
	}
}
