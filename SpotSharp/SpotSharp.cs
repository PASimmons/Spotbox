﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using libspotifydotnet;
using log4net;

namespace SpotSharp
{
    public class SpotSharp : IDisposable
    {
        private static readonly ILog _logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly Session session;

        private PlaylistContainer playlistContainer;

        private bool _shutDown;

        public SpotSharp(byte[] appkey)
        {
            session = new Session(this, appkey);

            new Task(StartMainSpotifyThread).Start();
        }

        public void Dispose()
        {
            playlistContainer.Dispose();
            
            session.Dispose();

            _shutDown = true;
        }

        public bool Login(string username, string password)
        {
            var loggedIn = session.Login(username, password);

            if (loggedIn)
            {
                LoggedInUser = session.GetCurrentUser();
                _logger.InfoFormat("Logged in as user: {0}", LoggedInUser.DisplayName);
                playlistContainer = new PlaylistContainer(session);
                _logger.InfoFormat("Found {0} playlists", playlistContainer.Playlists.Count);
                return true;
            }

            return false;
        }

        public bool LoggedIn
        {
            get
            {
                return libspotify.sp_session_connectionstate(session.SessionPtr) == libspotify.sp_connectionstate.LOGGED_IN;
            }
        }

        public void Logout()
        {
            if (libspotify.sp_session_connectionstate(session.SessionPtr) != libspotify.sp_connectionstate.LOGGED_OUT)
            {
                session.Logout();
                LoggedInUser = null;
            }
        }

        public TrackChangedDelegate TrackChanged;

        public delegate void TrackChangedDelegate(Track newTrack);

        public Playlist.PlaylistChangedDelegate PlaylistChanged;
        public User LoggedInUser { get; private set; }



        public void PlayDefaultPlaylist()
        {
            var playlistInfos = GetAllPlaylists();
            var playlistInfo = playlistInfos.FirstOrDefault(info => info.TrackCount > 0);
            if (playlistInfo == null)
            {
                _logger.WarnFormat("No playlists found with any tracks");
                return;
            }

            _logger.InfoFormat("Playing first playlist found");
            SetCurrentPlaylist(playlistInfo.Link);
            Play();
        }

        public Playlist GetPlaylist(Link playlistLink)
        {
            return playlistContainer.Playlists.FirstOrDefault(playlistInfo => playlistInfo.Link.Equals(playlistLink));
        }

        public List<Playlist> GetAllPlaylists()
        {
            return playlistContainer.Playlists;
        }

        public List<Track> SearchForTracks(string searchTerms)
        {
            var tracksFound = new List<Track>();
            searchCompleteDelegate = SearchComplete;
            var searchPtr = libspotify.sp_search_create(session.SessionPtr, searchTerms, 0, 10, 0, 0, 0, 0, 0, 0, sp_search_type.SP_SEARCH_STANDARD, Marshal.GetFunctionPointerForDelegate(searchCompleteDelegate), IntPtr.Zero);
            Wait.For(() => libspotify.sp_search_is_loaded(searchPtr) && libspotify.sp_search_error(searchPtr) == libspotify.sp_error.OK);

            var tracksFoundCount = libspotify.sp_search_num_tracks(searchPtr);
            for (var i = 0; i < tracksFoundCount; i++)
            {
                var track = new Track(libspotify.sp_search_track(searchPtr, i), session);
                tracksFound.Add(track);
            }

            return tracksFound;
        }

        public Playlist GetCurrentPlaylist()
        {
            return currentPlaylist;
        }

        public bool SetCurrentPlaylist(Link playlistLink)
        {
            var playlist = GetPlaylist(playlistLink);

            if (playlist == null)
            {
                libspotify.sp_playlistcontainer_add_playlist(playlistContainer.PlaylistContainerPtr, playlistLink.LinkPtr);
                Wait.For(() =>
                {
                    playlist = GetPlaylist(playlistLink);
                    return playlist != null;
                });
            }

            if (playlist == null)
            {
                return false;
            }

            if (currentPlaylist != null)
            {
                currentPlaylist.playlistChanged = null;
            }

            currentPlaylist = playlist;
            _logger.InfoFormat("Current playlist set to: {0}", currentPlaylist.Name);
            currentPlaylist.playlistChanged = PlaylistChanged;
            PlaylistChanged(currentPlaylist);
            return true;
        }

        private void StartMainSpotifyThread()
        {
            while (true)
            {
                if (_shutDown || session.SessionPtr == IntPtr.Zero)
                {
                    break;
                }

                int timeout;
                libspotify.sp_session_process_events(session.SessionPtr, out timeout);

                timeout = Math.Min(100, timeout);

                Thread.Sleep(timeout);
            }
        }

        private delegate void SearchCompleteDelegate(IntPtr searchPtr, IntPtr userDataPtr);

        private SearchCompleteDelegate searchCompleteDelegate;

        private Playlist currentPlaylist;

        private static void SearchComplete(IntPtr searchPtr, IntPtr userDataPtr)
        {
        }

        public void Play()
        {
            if (currentPlaylist != null)
            {
                currentPlaylist.Play();
            }
        }

        public void Unpause()
        {
            session.Unpause();
        }

        public void Pause()
        {
            session.Pause();
        }

        public void SetCurrentPlaylistPosition(int lastPosition)
        {
            if (currentPlaylist != null)
            {
                currentPlaylist.CurrentPosition = lastPosition;
            }
        }

        public void PlayNextTrack()
        {
            if (currentPlaylist != null)
            {
                currentPlaylist.PlayNextTrack();
            }
        }

        public void PlayPreviousTrack()
        {
            if (currentPlaylist != null)
            {
                currentPlaylist.PlayPreviousTrack();
            }
        }

        public Track GetCurrentTrack()
        {
            if (currentPlaylist != null)
            {
                return currentPlaylist.GetCurrentTrack();
            }
            return null;
        }
    }
}
