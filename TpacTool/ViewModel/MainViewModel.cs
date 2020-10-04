using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommonServiceLocator;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;
using GalaSoft.MvvmLight.Ioc;
using GalaSoft.MvvmLight.Messaging;
using GalaSoft.MvvmLight.Threading;
using Ookii.Dialogs.Wpf;
using TpacTool.Lib;
using TpacTool.Properties;
using Application = System.Windows.Application;
using Material = TpacTool.Lib.Material;
using TabItem = BetterWpfControls.TabItem;

namespace TpacTool
{
    public class MainViewModel : ViewModelBase
	{
		//private static readonly Uri Uri_Page_Empty = new Uri("");

		private static readonly Uri Uri_Page_Blank = new Uri("../Page/BlankPage.xaml", UriKind.Relative);

		private static readonly Uri Uri_Page_BlankPreview = new Uri("../Page/BlankPreviewPage.xaml", UriKind.Relative);

		private static readonly Uri Uri_Page_Model = new Uri("../Page/ModelPage.xaml", UriKind.Relative);

		//private static readonly Uri Uri_Page_ModelPreview = new Uri("../Page/ModelPreviewPage.xaml", UriKind.Relative);

		private static readonly Uri Uri_Page_Texture = new Uri("../Page/TexturePage.xaml", UriKind.Relative);

		private static readonly Uri Uri_Page_TexturePreview = new Uri("../Page/TexturePreviewPage.xaml", UriKind.Relative);

		private static readonly Uri Uri_Page_Material = new Uri("../Page/MaterialPage.xaml", UriKind.Relative);

		public static readonly Guid CleanupEvent = Guid.NewGuid();

		public static readonly Guid StatusEvent = Guid.NewGuid();

		private string _statusMsg = String.Empty;

		private LoadingWindow _loadingWindow;

		public Settings Settings { private set; get; }

		public AssetManager AssetManager { private set; get; }

		private VistaFolderBrowserDialog assetFolderDialog;

		public ICommand OpenAssetFolderCommand { set; get; }

		public ICommand OpenRecentFolderCommand { set; get; }

		public ICommand ChangeLanguageCommand { set; get; }

		public ICommand ShowAboutCommand { set; get; }

		public int RecentDirCount { private set; get; }

		public string[] RecentDirStrings { private set; get; }

		public bool IsReady { private set; get; } = false;

		public ObservableCollection<BetterWpfControls.TabItem> TabPages { private set; get; }

		public int SelectedIndex { set; get; } = -1;

		public Uri AssetPanelUri { set; get; }

		public Uri AssetPreviewUri { set; get; }

		public string StatusMsg
		{
			set
			{
				_statusMsg = value;
				RaisePropertyChanged("StatusMsg");
			}
			get { return _statusMsg; }
		}

		public MainViewModel()
		{
			TabPages = new ObservableCollection<BetterWpfControls.TabItem>();

			if (IsInDesignMode)
			{
				RecentDirCount = 9;
				RecentDirStrings = new string[9];
				for (var i = 0; i < RecentDirStrings.Length; i++)
				{
					RecentDirStrings[i] = "Recent " + (i + 1);
				}
			}
            else
            {
				Settings = Settings.Default;

				assetFolderDialog = new VistaFolderBrowserDialog();
				assetFolderDialog.Description = Resources.Main_Dialog_SelectAssetPackages;
				assetFolderDialog.UseDescriptionForTitle = true;
				assetFolderDialog.ShowNewFolderButton = false;

				RecentDirCount = Settings.Default.RecentWorkDirs.Count;
				RecentDirStrings = new string[9];
				for (var i = 0; i < RecentDirStrings.Length; i++)
				{
					RecentDirStrings[i] = String.Empty;
				}
				var rwd = Settings.Default.RecentWorkDirs;
				while (rwd.Count >= 10)
				{
					rwd.RemoveAt(rwd.Count - 1);
				}
				rwd.CopyTo(RecentDirStrings, 0);

				OpenAssetFolderCommand = new RelayCommand(OpenAssetFolder);
				OpenRecentFolderCommand = new RelayCommand<string>(OpenRecentFolder);
				ChangeLanguageCommand = new RelayCommand<string>(ChangeLanguage);
				ShowAboutCommand = new RelayCommand(ShowAbout);

				MessengerInstance.Register<AssetItem>(this, AssetTreeViewModel.AssetSelectedEvent, OnSelectAsset);

				MessengerInstance.Register<string>(this, StatusEvent, msg => StatusMsg = msg);

				/*var workDir = Path.GetDirectoryName(this.GetType().Assembly.Location);
				var assimpLib32 = workDir + "/bin/win-x86/native/assimp";
				var assimpLib64 = workDir + "/bin/win-x64/native/assimp";
				bool assimpLibLoaded = false;
				try
				{
					assimpLibLoaded = AssimpLibrary.Instance.LoadLibrary(assimpLib32, assimpLib64);
				}
				catch (AssimpException)
				{
				}
				if (!assimpLibLoaded)
				{
					ModelViewModel._model_exporter_unavailable = true;
					MessageBox.Show("Cannot init assimp. Model exporting is unavailable", "Warning",
						MessageBoxButton.OK, MessageBoxImage.Warning);
				}*/
			}
        }

		private void OpenAssetFolder()
		{
			if (assetFolderDialog.ShowDialog().GetValueOrDefault(false))
			{
				BeforeLoad();
				DirectoryInfo dir = new DirectoryInfo(assetFolderDialog.SelectedPath);
				if (!dir.Exists)
				{
					MessageBox.Show(Resources.Msgbox_DirNotExisting, Resources.Msgbox_Error,
						MessageBoxButton.OK, MessageBoxImage.Error);
				}
				else if (dir.GetFiles("*.tpac", SearchOption.TopDirectoryOnly).Length == 0)
				{
					MessageBox.Show(Resources.Msgbox_TpacNotFound, Resources.Msgbox_Info,
						MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				else
				{
					_loadingWindow = new LoadingWindow();
					_loadingWindow.Owner = Application.Current.MainWindow;
					AssetManager = new AssetManager();
					AssetManager.Load(dir, AssetManagerCallback);
					if (_loadingWindow.ShowDialog() == true)
					{
						Settings.Default.UpdateWorkDir(dir);
						Settings.Default.Save();
						RefreshRecents();
						AfterLoad();
					}
				}
			}
		}

		private void OpenRecentFolder(string obj)
		{
			BeforeLoad();
			int arg = int.Parse(obj);
			var rwd = Settings.Default.RecentWorkDirs;
			if (arg < rwd.Count)
			{
				var path = rwd[arg];
				DirectoryInfo dir = new DirectoryInfo(path);
				if (!dir.Exists)
				{
					rwd.Remove(path);
					Settings.Default.Save();
					RefreshRecents();
					MessageBox.Show(Resources.Msgbox_DirNotExisting, Resources.Msgbox_Error,
						MessageBoxButton.OK, MessageBoxImage.Error);
				}
				else if (dir.GetFiles("*.tpac", SearchOption.TopDirectoryOnly).Length == 0)
				{
					rwd.Remove(path);
					Settings.Default.Save();
					RefreshRecents();
					MessageBox.Show(Resources.Msgbox_TpacNotFound, Resources.Msgbox_Info,
						MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				else
				{
					_loadingWindow = new LoadingWindow();
					_loadingWindow.Owner = Application.Current.MainWindow;
					AssetManager = new AssetManager();
					AssetManager.Load(dir, AssetManagerCallback);
					if (_loadingWindow.ShowDialog() == true)
					{
						Settings.Default.UpdateWorkDir(dir);
						Settings.Default.Save();
						RefreshRecents();
						AfterLoad();
					}
				}
			}
		}

		private void BeforeLoad()
		{
			ModelViewModel._skeletons.Clear();
			ResourceCache.Cleanup();
			MessengerInstance.Send<object>(null, CleanupEvent);
			foreach (var tabItem in TabPages)
			{
				var page = tabItem.Content as AssetTreePage;
				if (page != null)
				{
					var vm = page.DataContext as AssetTreeViewModel;
					if (vm != null)
					{
						vm.Cleanup();
					}
				}
			}
			TabPages.Clear();
			DefaultDependenceResolver.Clear();
			GC.Collect();
		}

		private void AfterLoad()
		{
			AssetManager.SetAsDefaultGlobalResolver();

			string[] typeNames = new[]
			{
				Resources.Asset_Type_MorphAnimation,
				Resources.Asset_Type_SkeletalAnimation,
				Resources.Asset_Type_Skeleton,
				Resources.Asset_Type_PhysicsShape,
				Resources.Asset_Type_Model,
				Resources.Asset_Type_Material,
				Resources.Asset_Type_Texture,
				Resources.Asset_Type_Shader,
				Resources.Asset_Type_Particle,
				Resources.Asset_Type_ProceduralVectorField
			};

			Guid[] typeGuids = new[]
			{
				MorphAnimation.TYPE_GUID,
				SkeletalAnimation.TYPE_GUID,
				Skeleton.TYPE_GUID,
				PhysicsShape.TYPE_GUID,
				Metamesh.TYPE_GUID,
				Material.TYPE_GUID,
				Texture.TYPE_GUID,
				Shader.TYPE_GUID,
				Particle.TYPE_GUID,
				ProceduralVectorField.TYPE_GUID
			};

			Task<AssetTreeViewModel>[] tasks = new Task<AssetTreeViewModel>[typeNames.Length];
			for (var i = 0; i < typeNames.Length; i++)
			{
				int j = i;
				tasks[i] = Task.Run(() =>
				{
					var guid = typeGuids[j];
					AssetTreeViewModel vm = new AssetTreeViewModel(AssetManager, guid);
					return vm;
				});
			}

			MessengerInstance.Send(AssetManager.LoadedAssets.Where(asset =>
			{
				return asset.Type == Skeleton.TYPE_GUID && !asset.Name.Contains("notused");
			}).Cast<Skeleton>(), ModelViewModel.UpdateSkeletonListEvent);
			for (var i = 0; i < typeNames.Length; i++)
			{
				var name = typeNames[i];
				tasks[i].Wait();
				if (tasks[i].Exception != null)
					throw tasks[i].Exception;
				AssetTreePage page = new AssetTreePage();
				page.DataContext = tasks[i].Result;
				TabPages.Add(new BetterWpfControls.TabItem() { Header = name, Content = page });
			}

			SelectedIndex = 4; // select model page
			RaisePropertyChanged("SelectedIndex");
			GC.Collect();
		}

		private void RefreshRecents()
		{
			RecentDirCount = Settings.Default.RecentWorkDirs.Count;
			RecentDirStrings = new string[9];
			for (var i = 0; i < RecentDirStrings.Length; i++)
			{
				RecentDirStrings[i] = String.Empty;
			}
			Settings.Default.RecentWorkDirs.CopyTo(RecentDirStrings, 0);
			RaisePropertyChanged("RecentDirCount");
			RaisePropertyChanged("RecentDirStrings");
		}

		private void AssetManagerCallback(int package, int packagecount, string filename, bool completed)
		{
			DispatcherHelper.CheckBeginInvokeOnUI(() =>
			{
				if (!completed)
					MessengerInstance.Send(ValueTuple.Create(package, packagecount, filename), 
											LoadingViewModel.LoadingProgressEvent);
				else
				{
					_loadingWindow.DialogResult = true;
					_loadingWindow.Close();
				}
			});
		}

		private void OnSelectAsset(AssetItem asset)
		{
			if (asset == null)
			{
				AssetPanelUri = null;
				AssetPreviewUri = null;
				RaisePropertyChanged("AssetPanelUri");
				RaisePropertyChanged("AssetPreviewUri");
				return;
			}

			bool hasContent = false;

			var metamesh = asset as Metamesh;
			if (metamesh != null)
			{
				if (AssetPanelUri != Uri_Page_Model)
				{
					AssetPanelUri = Uri_Page_Model;
					AssetPreviewUri = ServiceLocator.Current.GetInstance<WpfPreviewViewModel>().PageUri;
				}
				hasContent = true;
			}

			var texture = asset as Texture;
			if (texture != null)
			{
				if (AssetPanelUri != Uri_Page_Texture)
				{
					AssetPanelUri = Uri_Page_Texture;
					AssetPreviewUri = Uri_Page_TexturePreview;
				}
				hasContent = true;
			}

			var material = asset as Material;
			if (material != null)
			{
				if (AssetPanelUri != Uri_Page_Material)
				{
					AssetPanelUri = Uri_Page_Material;
					AssetPreviewUri = Uri_Page_TexturePreview;
				}
				hasContent = true;
			}

			if (!hasContent)
			{
				if (AssetPanelUri != Uri_Page_Blank)
					AssetPanelUri = Uri_Page_Blank;
				if (AssetPreviewUri != Uri_Page_BlankPreview)
					AssetPreviewUri = Uri_Page_BlankPreview;
			}

			RaisePropertyChanged("AssetPanelUri");
			RaisePropertyChanged("AssetPreviewUri");
		}

		private void ChangeLanguage(string obj)
		{
			App.SetLanguage(obj);
			RaisePropertyChanged("Settings.Language");
		}

		private void ShowAbout()
		{
			var about = new AboutWindow();
			about.Owner = Application.Current.MainWindow;
			about.ShowDialog();
		}

		/*public sealed class TabItem
		{
			public string Header { set; get; }
			public Page Content { set; get; }
		}*/
	}
}