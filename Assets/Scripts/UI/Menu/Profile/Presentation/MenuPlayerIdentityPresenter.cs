#nullable enable

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Kern.Core;
using Kern.Core.Interfaces;
using Kern.Core.Localization;
using Kern.Networking;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using VContainer;
using Stopwatch = System.Diagnostics.Stopwatch;
using Kern.Networking.Gateways;
using Kern.Networking.Status;
using Kern.UI.Localization;
using Kern.UI.Screens;

namespace Kern.UI.Menu;

internal sealed class MenuPlayerIdentityPresenter
{
    private readonly ILocalizationService _loc;
    private readonly IPlayerStats _playerStats;

    internal MenuPlayerIdentityPresenter(ILocalizationService localization, IPlayerStats playerStats)
    {
        _loc = localization;
        _playerStats = playerStats;
    }

    internal void Bind(VisualElement tree)
    {
        _userNameLabel = tree.Q<Label>(className: "mm-user-name");
        _userRankLabel = tree.Q<Label>(className: "mm-user-rank");
        _profileNameLabel = tree.Q<Label>(className: "mm-profile-name");
        _profileClanLabel = tree.Q<Label>(className: "mm-profile-clan");
    }

    internal void Dispose()
    {
        if (_playerIdentitySubscribed)
        {
            _playerStats.OnNicknameChanged -= ApplyPlayerIdentity;
            _playerStats.OnStatsChanged -= ApplyPlayerIdentity;
            _playerIdentitySubscribed = false;
        }
    }


        internal Label? _userNameLabel;
        internal Label? _userRankLabel;
        internal Label? _profileNameLabel;
        internal Label? _profileClanLabel;
        internal bool _playerIdentitySubscribed;

        /// <summary>
        /// Живые данные шахтёра в бейдже и досье: ник и уровень приходят по
        /// сокету (PlayerIdentityProcessor) ещё в меню. Пустые значения оставляют
        /// локализационные заглушки, чтобы не мигать пустотой до первого пакета.
        /// </summary>
        internal void SubscribePlayerIdentity()
        {
            if (_playerStats == null || _playerIdentitySubscribed)
            {
                return;
            }

            _playerStats.OnNicknameChanged += ApplyPlayerIdentity;
            _playerStats.OnStatsChanged += ApplyPlayerIdentity;
            _playerIdentitySubscribed = true;
            ApplyPlayerIdentity();
        }

        internal void ApplyPlayerIdentity()
        {
            if (_userNameLabel == null || _playerStats == null || _loc == null)
            {
                return;
            }

            string nickname = _playerStats.Nickname;
            if (!string.IsNullOrWhiteSpace(nickname))
            {
                _userNameLabel.text = nickname;
                if (_profileNameLabel != null)
                {
                    _profileNameLabel.text = nickname;
                }
            }

            // В строке ранга остаётся только уровень, без ранга и тира бура.
            long level = _playerStats.Level;
            if (level <= 0)
            {
                return;
            }

            if (_userRankLabel != null)
            {
                _userRankLabel.text = level.ToString();
            }

            // В досье то же самое: строка клана хвостит уровень после «//».
            if (_profileClanLabel != null)
            {
                string clan = _loc.Get("mainmenu.profile_clan");
                int separator = clan.IndexOf("//", StringComparison.Ordinal);
                _profileClanLabel.text = separator >= 0
                    ? clan[..separator] + "// " + level
                    : level.ToString();
            }
        }

}
