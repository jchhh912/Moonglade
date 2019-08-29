﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using Edi.Practice.RequestResponseModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moonglade.Configuration.Abstraction;
using Moonglade.Core;
using Moonglade.Model;
using Moonglade.Model.Settings;

namespace Moonglade.Notification
{
    public class EmailNotificationClient : IMoongladeNotificationClient
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public bool IsEnabled { get; set; }

        private readonly ILogger<EmailNotificationClient> _logger;

        private readonly IBlogConfig _blogConfig;

        public EmailNotificationClient(
            ILogger<EmailNotificationClient> logger,
            IOptions<AppSettings> settings,
            IBlogConfig blogConfig,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _blogConfig = blogConfig;
            if (settings.Value.Notification.Enabled)
            {
                _httpClientFactory = httpClientFactory;
            }

            IsEnabled = null != _httpClientFactory && _blogConfig.EmailSettings.EnableEmailSending;
        }

        public async Task<Response> SendTestNotificationAsync()
        {
            throw new NotImplementedException();
        }

        public async Task SendNewCommentNotificationAsync(CommentListItem comment, Func<string, string> funcCommentContentFormat)
        {
            throw new NotImplementedException();
        }

        public async Task SendCommentReplyNotificationAsync(CommentReplyDetail model, string postLink)
        {
            throw new NotImplementedException();
        }

        public async Task SendPingNotificationAsync(PingbackHistory receivedPingback)
        {
            throw new NotImplementedException();
        }
    }
}