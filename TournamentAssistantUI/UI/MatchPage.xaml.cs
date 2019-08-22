﻿using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using TournamentAssistantShared;
using TournamentAssistantShared.Models;
using TournamentAssistantShared.Models.Packets;
using TournamentAssistantUI.BeatSaver;

namespace TournamentAssistantUI.UI
{
    /// <summary>
    /// Interaction logic for MatchPage.xaml
    /// </summary>
    public partial class MatchPage : Page, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void NotifyPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private Match _match;
        public Match Match
        {
            get
            {
                return _match;
            }
            set
            {
                _match = value;
                NotifyPropertyChanged(nameof(Match));
            }
        }

        private double _loadSongButtonProgress;
        public double LoadSongButtonProgress
        {
            get
            {
                return _loadSongButtonProgress;
            }
            set
            {
                _loadSongButtonProgress = value;
                NotifyPropertyChanged(nameof(LoadSongButtonProgress));
            }
        }

        private bool _songLoading;
        public bool SongLoading
        {
            get
            {
                return _songLoading;
            }
            set
            {
                _songLoading = value;
                NotifyPropertyChanged(nameof(SongLoading));
            }
        }


        public MainPage MainPage{ get; set; }

        public ICommand LoadSong { get; }
        public ICommand PlaySong { get; }
        public ICommand ClosePage { get; }
        public ICommand DestroyAndCloseMatch { get; }

        public MatchPage(Match match, MainPage mainPage)
        {
            InitializeComponent();

            DataContext = this;

            Match = match;
            MainPage = mainPage;

            //Due to my inability to use a custom converter to successfully use DataBinding to accomplish this same goal,
            //we are left doing it this weird gross way
            SongBox.Dispatcher.Invoke(() => SongBox.IsEnabled = Match.CurrentlySelectedMap != null);

            //If the match info is updated, we need to catch that and show the changes on this page
            MainPage.Connection.MatchInfoUpdated += Connection_MatchInfoUpdated;

            //If the match is externally deleted, we need to close the page
            MainPage.Connection.MatchDeleted += Connection_MatchDeleted;

            LoadSong = new CommandImplementation(LoadSong_Executed, LoadSong_CanExecute);
            PlaySong = new CommandImplementation(PlaySong_Executed, PlaySong_CanExecute);
            ClosePage = new CommandImplementation(ClosePage_Executed, (_) => true);
            DestroyAndCloseMatch = new CommandImplementation(DestroyAndCloseMatch_Executed, (_) => true);

            //Set up log monitor
            Logger.MessageLogged += (type, message) =>
            {
                SolidColorBrush textBrush = null;
                switch (type)
                {
                    case Logger.LogType.Debug:
                        textBrush = Brushes.LightSkyBlue;
                        break;
                    case Logger.LogType.Error:
                        textBrush = Brushes.Red;
                        break;
                    case Logger.LogType.Info:
                        textBrush = Brushes.White;
                        break;
                    case Logger.LogType.Success:
                        textBrush = Brushes.Green;
                        break;
                    case Logger.LogType.Warning:
                        textBrush = Brushes.Yellow;
                        break;
                    default:
                        break;
                }

                LogBlock.Dispatcher.Invoke(() => LogBlock.Inlines.Add(new Run($"{message}\n") { Foreground = textBrush }));
            };
        }

        private void Connection_MatchInfoUpdated(Match updatedMatch)
        {
            if (updatedMatch.Guid == Match.Guid)
            {
                Match = updatedMatch;
                NotifyPropertyChanged(nameof(Match));

                //If the Match has a song now, be super sure the song box is enabled
                if (Match.CurrentlySelectedMap != null) SongBox.Dispatcher.Invoke(() => SongBox.IsEnabled = true);
            }
        }

        private void Connection_MatchDeleted(Match deletedMatch)
        {
            if (deletedMatch.Guid == Match.Guid)
            {
                Dispatcher.Invoke(() => ClosePage.Execute(this));
            }
        }

        private void LoadSong_Executed(object obj)
        {
            SongLoading = true;
            var songId = GetSongIdFromUrl(SongUrlBox.Text);
            var hash = BeatSaverDownloader.GetHashFromID(songId);

            BeatSaverDownloader.DownloadSongInfoThreaded(hash,
                (successfulDownload) =>
                {
                    SongLoading = false;
                    LoadSongButtonProgress = 0;
                    if (successfulDownload)
                    {
                        var song = new Song(hash);
                        var matchMap = new PreviewBeatmapLevel()
                        {
                            LevelId = hash,
                            Name = song.Name
                        };

                        List<Characteristic> characteristics = new List<Characteristic>();
                        foreach (var characteristic in song.Characteristics)
                        {
                            characteristics.Add(new Characteristic()
                            {
                                SerializedName = characteristic.ToString(), //TODO: Is this right? Is this really the equivilant of a "SerializedName"?
                                Difficulties = song.GetBeatmapDifficulties(characteristic)
                            });
                        }
                        matchMap.Characteristics = characteristics.ToArray();

                        Match.CurrentlySelectedMap = matchMap;
                        Match.CurrentlySelectedCharacteristic = null;
                        Match.CurrentlySelectedDifficulty = SharedConstructs.BeatmapDifficulty.Easy; //Easy, aka 0, aka null

                        //Notify all the UI that needs to be notified, and propegate the info across the network
                        NotifyPropertyChanged(nameof(Match));
                        MainPage.Connection.UpdateMatch(Match);
                    }

                    //Due to my inability to use a custom converter to successfully use DataBinding to accomplish this same goal,
                    //we are left doing it this weird gross way
                    SongBox.Dispatcher.Invoke(() => SongBox.IsEnabled = true);
                },
                (progress) =>
                {
                    LoadSongButtonProgress = progress;
                });
        }

        private bool LoadSong_CanExecute(object arg) => !SongLoading && GetSongIdFromUrl(SongUrlBox.Text) != null;

        private string GetSongIdFromUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            //Sanitize input
            if (url.StartsWith("https://beatsaver.com/") || url.StartsWith("https://bsaber.com/"))
            {
                //Strip off the trailing slash if there is one
                if (url.EndsWith("/")) url = url.Substring(0, url.Length - 1);

                //Strip off the beginning of the url to leave the id
                url = url.Substring(url.LastIndexOf("/") + 1);
            }

            if (url.Contains("&"))
            {
                url = url.Substring(0, url.IndexOf("&"));
            }

            return url.Length == 4 ? url : null;
        }

        private void PlaySong_Executed(object obj)
        {
            var playSong = new PlaySong();
            playSong.characteristic = new Characteristic();
            playSong.characteristic.SerializedName = Match.CurrentlySelectedCharacteristic.SerializedName;
            playSong.difficulty = Match.CurrentlySelectedDifficulty;
            playSong.gameplayModifiers = new GameplayModifiers();
            playSong.playerSettings = new PlayerSpecificSettings();
            playSong.levelId = Match.CurrentlySelectedMap.LevelId;

            MainPage.Connection.Send(Match.Players.Select(x => x.Guid).ToArray(), new Packet(playSong));
        }

        private bool PlaySong_CanExecute(object arg) => !SongLoading && DifficultyDropdown.SelectedItem != null;

        private void DestroyAndCloseMatch_Executed(object obj)
        {
            if (MainPage.DestroyMatch.CanExecute(Match)) MainPage.DestroyMatch.Execute(Match);
        }

        private void ClosePage_Executed(object obj)
        {
            MainPage.Connection.MatchInfoUpdated -= Connection_MatchInfoUpdated;
            MainPage.Connection.MatchDeleted -= Connection_MatchDeleted;

            var navigationService = NavigationService.GetNavigationService(this);
            navigationService.GoBack();
        }

        private void CharacteristicBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if ((sender as ComboBox).SelectedItem != null)
            {
                var oldCharacteristic = Match.CurrentlySelectedCharacteristic;

                Match.CurrentlySelectedCharacteristic = Match.CurrentlySelectedMap.Characteristics.First(x => x.SerializedName == (sender as ComboBox).SelectedItem.ToString());

                //When we update the match, we actually get back an UpdateMatch event which causes this very same event again...
                //Usually I handle this infinite recursion by letting the Events control all the user controls, but that's
                //not possible in this case. So here, we're specifically not going to send an UpdateMatch event if
                //nothing changed because of the selection. It's hacky, and doesn't prevent *all* of the technically excess events
                //from being sent, but it works, so it's here.
                if (Match.CurrentlySelectedCharacteristic != oldCharacteristic) MainPage.Connection.UpdateMatch(Match);
                NotifyPropertyChanged(nameof(Match));
            }
        }

        private void DifficultyDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DifficultyDropdown.SelectedItem != null)
            {
                var oldDifficulty = Match.CurrentlySelectedDifficulty;
                
                Match.CurrentlySelectedDifficulty = Match.CurrentlySelectedCharacteristic.Difficulties.First(x => x.ToString() == DifficultyDropdown.SelectedItem.ToString()); ;

                //When we update the match, we actually get back an UpdateMatch event which causes this very same event again...
                //Usually I handle this infinite recursion by letting the Events control all the user controls, but that's
                //not possible in this case. So here, we're specifically not going to send an UpdateMatch event if
                //nothing changed because of the selection. It's hacky, and doesn't prevent *all* of the technically excess events
                //from being sent, but it works, so it's here.
                if (Match.CurrentlySelectedDifficulty != oldDifficulty) MainPage.Connection.UpdateMatch(Match);
                NotifyPropertyChanged(nameof(Match));
            }
        }
    }
}
