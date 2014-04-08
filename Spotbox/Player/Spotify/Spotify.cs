﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using libspotifydotnet;

namespace Spotbox.Player.Spotify
{
    public class Spotify
    {
        public delegate void LibSpotifyThreadMessageDelegate(object[] args);

        private static AutoResetEvent _programSignal;
        private static AutoResetEvent _mainSignal;
        private static Queue<LibSpotifyThreadMessage> _libSpotifyMessageQueue = new Queue<LibSpotifyThreadMessage>();
        private static bool _shutDown;
        private static object _syncObj = new object();
        private static object _initSync = new object();
        private static bool _initialised;
        private static Action<IntPtr> d_notify = Session_OnNotifyMainThread;
        private static Action<IntPtr> d_on_logged_in = Session_OnLoggedIn;
        private static Thread _libSpotifyThread;

        private class LibSpotifyThreadMessage
        {
            public LibSpotifyThreadMessageDelegate d;
            public object[] payload;
        }

        public static bool Login(byte[] appkey, string username, string password)
        {
            InitializeLibSpotifyThread();

            Console.WriteLine("Logging into spotify with credentials");
            Console.WriteLine("Username: {0}", username);
            Console.WriteLine("Password: {0}", new String('*', password.Length));

            PostMessage(Session.Login, new object[] { appkey, username, password });

            _programSignal.WaitOne();

            if (Session.LoginError != libspotify.sp_error.OK)
            {
                Console.WriteLine("Login failed: {0}", libspotify.sp_error_message(Session.LoginError));
                return false;
            }

            return true;
        }

        private static void InitializeLibSpotifyThread()
        {
            if (_initialised)
                return;

            lock (_initSync)
            {
                try
                {
                    Session.OnNotifyMainThread += d_notify;
                    Session.OnLoggedIn += d_on_logged_in;

                    _programSignal = new AutoResetEvent(false);

                    _libSpotifyThread = new Thread(StartMainSpotifyThread);
                    _libSpotifyThread.Start();

                    _programSignal.WaitOne();

                    Console.WriteLine("Main spotify thread running...");

                    _initialised = true;
                }
                catch
                {
                    Session.OnNotifyMainThread -= d_notify;
                    Session.OnLoggedIn -= d_on_logged_in;

                    if (_libSpotifyThread != null)
                    {
                        try
                        {
                            _libSpotifyThread.Abort();
                        }
                        catch { }
                        finally
                        {
                            _libSpotifyThread = null;
                        }
                    }
                }
            }
        }

        public static void PlayDefaultPlaylist()
        {
            var playlistInfos = GetAllPlaylists();
            var playlistInfo = playlistInfos.Where(info => info.TrackCount > 0).Skip(1).FirstOrDefault();
            Console.WriteLine("Playing first playlist found");
            var playlist = new Playlist(playlistInfo.PlaylistPtr);

            Audio.SetPlaylist(playlist);
            Audio.Play();
        }

        public static List<PlaylistInfo> GetAllPlaylists()
        {
            var playlistContainer = GetSessionUserPlaylists();
            Console.WriteLine("Found {0} playlists", playlistContainer.PlaylistInfos.Count);
            return playlistContainer.PlaylistInfos;
        }

        public static PlaylistContainer GetSessionUserPlaylists()
        {
            if (Session.GetSessionPtr() == IntPtr.Zero)
                throw new InvalidOperationException("No valid session.");

            return new PlaylistContainer(libspotify.sp_session_playlistcontainer(Session.GetSessionPtr()));
        }

        public static void ShutDown()
        {
            lock (_syncObj)
            {
                libspotify.sp_session_player_unload(Session.GetSessionPtr());
                libspotify.sp_session_logout(Session.GetSessionPtr());

                try
                {
                    if (Libspotifydotnet.PlaylistContainer.GetSessionContainer() != null)
                    {
                        Libspotifydotnet.PlaylistContainer.GetSessionContainer().Dispose();
                    }
                }
                catch { }

                if (_mainSignal != null)
                    _mainSignal.Set();
                _shutDown = true;
            }

            _programSignal.WaitOne(2000, false);
        }

        private static void StartMainSpotifyThread()
        {
            try
            {
                _mainSignal = new AutoResetEvent(false);

                var timeout = Timeout.Infinite;

                _programSignal.Set(); // this signals to program thread that loop is running   

                while (true)
                {
                    if (_shutDown)
                        break;

                    _mainSignal.WaitOne(timeout, false);

                    if (_shutDown)
                        break;

                    lock (_syncObj)
                    {
                        try
                        {
                            if (Session.GetSessionPtr() != IntPtr.Zero)
                            {
                                do
                                {
                                    libspotify.sp_session_process_events(Session.GetSessionPtr(), out timeout);
                                } while (timeout == 0);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("Exception invoking sp_session_process_events {0}", ex);
                        }

                        while (_libSpotifyMessageQueue.Count > 0)
                        {
                            var m = _libSpotifyMessageQueue.Dequeue();
                            m.d.Invoke(m.payload);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("StartMainSpotifyThread() unhandled exception: {0}", ex);
            }
            finally
            {
                if (_programSignal != null)
                    _programSignal.Set();
            }
        }

        public static void Session_OnLoggedIn(IntPtr obj)
        {
            _programSignal.Set();
        }

        public static void Session_OnNotifyMainThread(IntPtr sessionPtr)
        {
            _mainSignal.Set();
        }

        private static void PostMessage(LibSpotifyThreadMessageDelegate d, object[] payload)
        {
            _libSpotifyMessageQueue.Enqueue(new LibSpotifyThreadMessage { d = d, payload = payload });

            lock (_syncObj)
            {
                _mainSignal.Set();
            }
        }
    }
}
