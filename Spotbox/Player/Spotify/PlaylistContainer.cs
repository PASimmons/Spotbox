﻿using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using libspotifydotnet;

using log4net;

namespace Spotbox.Player.Spotify
{
    public class PlaylistContainer
    {
        private static readonly ILog _logger = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private IntPtr _callbacksPtr;

        public IntPtr PlaylistContainerPtr { get; private set; }
        
        public List<PlaylistInfo> PlaylistInfos { get; private set; }

        public PlaylistContainer(IntPtr playlistContainerPtr)
        {
            PlaylistContainerPtr = playlistContainerPtr;
            AddCallbacks();
            Wait.For(IsLoaded, 10);

            LoadPlaylistInfos();
        }
  
        public void Dispose()
        {
            GC.SuppressFinalize(this);
            libspotify.sp_playlistcontainer_remove_callbacks(PlaylistContainerPtr, _callbacksPtr, IntPtr.Zero);
        }

        private bool IsLoaded()
        {
            return libspotify.sp_playlistcontainer_is_loaded(PlaylistContainerPtr);
        }

        private void LoadPlaylistInfos()
        {
            PlaylistInfos = new List<PlaylistInfo>();

            var playlistCount = libspotify.sp_playlistcontainer_num_playlists(PlaylistContainerPtr);

            for (var i = 0; i < playlistCount; i++)
            {
                var playlistType = libspotify.sp_playlistcontainer_playlist_type(PlaylistContainerPtr, i);
                if ( playlistType != libspotify.sp_playlist_type.SP_PLAYLIST_TYPE_PLAYLIST)
                {
                    continue;
                }

                var playlistPtr = libspotify.sp_playlistcontainer_playlist(PlaylistContainerPtr, i);
                var playlistInfo = new PlaylistInfo(playlistPtr);
                PlaylistInfos.Add(playlistInfo);
            }
        }

        #region Callbacks

        private delegate void ContainerLoadedDelegate(IntPtr containerPtr, IntPtr userDataPtr);
        private delegate void PlaylistAddedDelegate(IntPtr containerPtr, IntPtr playlistPtr, int position, IntPtr userDataPtr);
        private delegate void PlaylistMovedDelegate(IntPtr containerPtr, IntPtr playlistPtr, int oldPosition, int newPosition, IntPtr userDataPtr);
        private delegate void PlaylistRemovedDelegate(IntPtr containerPtr, IntPtr playlistPtr, int position, IntPtr userDataPtr);

        private ContainerLoadedDelegate _containerLoadedDelegate;
        private PlaylistAddedDelegate _playlistAddedDelegate;
        private PlaylistMovedDelegate _playlistMovedDelegate;
        private PlaylistRemovedDelegate _playlistRemovedDelegate;

        private void AddCallbacks()
        {
            _containerLoadedDelegate = PlayListContainerLoaded;
            _playlistAddedDelegate = PlaylistAdded;
            _playlistMovedDelegate = PlaylistMoved;
            _playlistRemovedDelegate = PlaylistRemoved;

            var playlistcontainerCallbacks = new libspotify.sp_playlistcontainer_callbacks
            {
                container_loaded = Marshal.GetFunctionPointerForDelegate(_containerLoadedDelegate),
                playlist_added = Marshal.GetFunctionPointerForDelegate(_playlistAddedDelegate),
                playlist_moved = Marshal.GetFunctionPointerForDelegate(_playlistMovedDelegate),
                playlist_removed = Marshal.GetFunctionPointerForDelegate(_playlistRemovedDelegate)
            };

            _callbacksPtr = Marshal.AllocHGlobal(Marshal.SizeOf(playlistcontainerCallbacks));
            Marshal.StructureToPtr(playlistcontainerCallbacks, _callbacksPtr, true);
            libspotify.sp_playlistcontainer_add_callbacks(PlaylistContainerPtr, _callbacksPtr, IntPtr.Zero);
        }

        private void PlayListContainerLoaded(IntPtr containerPtr, IntPtr userDataPtr) { }

        private void PlaylistAdded(IntPtr containerPtr, IntPtr playlistPtr, int position, IntPtr userDataPtr)
        {
            PlaylistInfos.Insert(position, new PlaylistInfo(playlistPtr));
            _logger.InfoFormat("Playlist added at position {0}", position);
        }

        private void PlaylistMoved(IntPtr containerPtr, IntPtr playlistPtr, int oldPosition, int newPosition, IntPtr userDataPtr)
        {
            var item = PlaylistInfos[oldPosition];

            PlaylistInfos.RemoveAt(oldPosition);

            if (newPosition > oldPosition)
            {
                newPosition--;
            }

            PlaylistInfos.Insert(newPosition, item);
            _logger.InfoFormat("Playlist moved from {0} to {1}", oldPosition, newPosition);
        }

        private void PlaylistRemoved(IntPtr containerPtr, IntPtr playlistPtr, int position, IntPtr userDataPtr)
        {
            PlaylistInfos.RemoveAt(position);
            _logger.InfoFormat("Playlist Removed from position {0}", position);
        }

        #endregion
    }
}
