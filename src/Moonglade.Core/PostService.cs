﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Edi.Practice.RequestResponseModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moonglade.Data.Entities;
using Moonglade.Data.Infrastructure;
using Moonglade.Data.Spec;
using Moonglade.Model;
using Moonglade.Model.Settings;

namespace Moonglade.Core
{
    public class PostService : MoongladeService
    {
        private readonly IRepository<PostEntity> _postRepository;

        private readonly IRepository<PostExtensionEntity> _postExtensionRepository;

        private readonly IRepository<PostPublishEntity> _postPublishRepository;

        private readonly IRepository<TagEntity> _tagRepository;

        private readonly IRepository<CategoryEntity> _categoryRepository;

        private readonly IRepository<PostCategoryEntity> _postCategoryRepository;

        public PostService(ILogger<PostService> logger,
            IOptions<AppSettings> settings,
            IRepository<PostEntity> postRepository,
            IRepository<PostExtensionEntity> postExtensionRepository,
            IRepository<TagEntity> tagRepository,
            IRepository<PostPublishEntity> postPublishRepository,
            IRepository<CategoryEntity> categoryRepository,
            IRepository<PostCategoryEntity> postCategoryRepository) : base(logger, settings)
        {
            _postRepository = postRepository;
            _postExtensionRepository = postExtensionRepository;
            _tagRepository = tagRepository;
            _postPublishRepository = postPublishRepository;
            _categoryRepository = categoryRepository;
            _postCategoryRepository = postCategoryRepository;
        }

        public int CountForPublic => _postPublishRepository.Count(p => p.IsPublished && !p.IsDeleted);

        public int CountByCategoryId(Guid catId)
        {
            return _postCategoryRepository.Count(c => c.CategoryId == catId);
        }

        public async Task<Response> UpdatePostStatisticAsync(Guid postId, StatisticTypes statisticTypes)
        {
            return await TryExecuteAsync(async () =>
            {
                var pp = _postExtensionRepository.Get(postId);
                if (pp == null) return new FailedResponse((int)ResponseFailureCode.PostNotFound);

                if (statisticTypes == StatisticTypes.Hits)
                {
                    pp.Hits += 1;
                }
                if (statisticTypes == StatisticTypes.Likes)
                {
                    pp.Likes += 1;
                }

                await _postExtensionRepository.UpdateAsync(pp);
                return new SuccessResponse();
            }, keyParameter: postId);
        }

        public Response<PostEntity> GetPost(Guid id)
        {
            try
            {
                var spec = new PostSpec(id);
                var post = _postRepository.GetFirstOrDefault(spec);
                return new SuccessResponse<PostEntity>(post);
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error {nameof(GetPost)}(id: {id})");
                return new FailedResponse<PostEntity>((int)ResponseFailureCode.GeneralException, e.Message);
            }
        }

        public Response<(Guid Id, string Title)> GetPostIdTitle(string url)
        {
            try
            {
                var response = Utils.GetSlugInfoFromPostUrl(url);
                if (!response.IsSuccess)
                {
                    return null;
                }

                var post = _postRepository.Get(p => p.Slug == response.Item.Slug &&
                                               p.PostPublish.PubDateUtc.GetValueOrDefault().Date == response.Item.PubDate.Date &&
                                               p.PostPublish.IsPublished &&
                                               !p.PostPublish.IsDeleted);

                return null == post ?
                    new FailedResponse<(Guid, string)>((int)ResponseFailureCode.PostNotFound) :
                    new Response<(Guid, string)>((post.Id, post.Title));
            }
            catch (Exception ex)
            {
                return new FailedResponse<(Guid, string)>((int)ResponseFailureCode.GeneralException, ex.Message, ex);
            }
        }

        public async Task<Response<PostEntity>> GetPostAsync(int year, int month, int day, string slug)
        {
            return await TryExecuteAsync<PostEntity>(async () =>
            {
                var date = new DateTime(year, month, day);
                var spec = new PostSpec(date, slug);
                var post = await _postRepository.GetFirstOrDefaultAsync(spec, false);

                return new SuccessResponse<PostEntity>(post);
            });
        }

        public Task<IReadOnlyList<PostMetaData>> GetPostMetaListAsync(bool isDeleted = false, bool? isPublished = true)
        {
            var spec = null != isPublished ? new PostSpec(isDeleted, isPublished.Value) : new PostSpec();
            return _postRepository.SelectAsync(spec, p => new PostMetaData
            {
                Id = p.Id,
                Title = p.Title,
                PubDateUtc = p.PostPublish.PubDateUtc,
                IsPublished = p.PostPublish.IsPublished,
                IsDeleted = p.PostPublish.IsDeleted,
                Revision = p.PostPublish.Revision,
                CreateOnUtc = p.CreateOnUtc.Value,
                Hits = p.PostExtension.Hits
            });
        }

        public Task<IReadOnlyList<PostListItem>> GetPagedPostsAsync(int pageSize, int pageIndex, Guid? categoryId = null)
        {
            if (pageSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageSize),
                    $"{nameof(pageSize)} can not be less than 1, current value: {pageSize}.");
            }
            if (pageIndex < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(pageIndex),
                    $"{nameof(pageIndex)} can not be less than 1, current value: {pageIndex}.");
            }

            var spec = new PostSpec(pageSize, pageIndex, categoryId);
            return _postRepository.SelectAsync(spec, p => new PostListItem
            {
                Title = p.Title,
                Slug = p.Slug,
                ContentAbstract = p.ContentAbstract,
                PubDateUtc = p.PostPublish.PubDateUtc.GetValueOrDefault(),
                Tags = p.PostTag.Select(pt => new TagInfo
                {
                    NormalizedTagName = pt.Tag.NormalizedName,
                    TagName = pt.Tag.DisplayName
                }).ToList()
            });
        }

        public async Task<IReadOnlyList<PostArchiveItem>> GetArchivedPostsAsync(int year, int month = 0)
        {
            if (year < DateTime.MinValue.Year || year > DateTime.MaxValue.Year)
            {
                throw new ArgumentOutOfRangeException(nameof(year));
            }

            if (month > 12 || month < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(month));
            }

            var spec = new PostSpec(year, month);
            var list = await _postRepository.SelectAsync(spec, p => new PostArchiveItem
            {
                PubDateUtc = p.PostPublish.PubDateUtc.GetValueOrDefault(),
                Slug = p.Slug,
                Title = p.Title
            });
            return list;
        }

        public async Task<Response<IReadOnlyList<PostEntity>>> GetPostsByTagAsync(string normalizedName)
        {
            return await TryExecuteAsync<IReadOnlyList<PostEntity>>(async () =>
            {
                if (string.IsNullOrWhiteSpace(normalizedName))
                {
                    throw new ArgumentNullException(nameof(normalizedName));
                }

                var posts = await _tagRepository.GetAsQueryable()
                                                .Where(t => t.NormalizedName == normalizedName)
                                                .SelectMany(p => p.PostTag)
                                                .Select(p => p.Post)
                                                .Include(p => p.PostPublish).ToListAsync();

                return new SuccessResponse<IReadOnlyList<PostEntity>>(posts);
            });
        }

        #region Search

        public async Task<Response<IReadOnlyList<PostListItem>>> SearchPostAsync(string keyword)
        {
            return await TryExecuteAsync<IReadOnlyList<PostListItem>>(async () =>
            {
                if (string.IsNullOrWhiteSpace(keyword))
                {
                    throw new ArgumentNullException(keyword);
                }

                var postList = SearchPostByKeyword(keyword);

                var resultList = await postList.Select(p => new PostListItem
                {
                    Title = p.Title,
                    Slug = p.Slug,
                    ContentAbstract = p.ContentAbstract,
                    PubDateUtc = p.PostPublish.PubDateUtc.GetValueOrDefault(),
                    Tags = p.PostTag.Select(pt => new TagInfo
                    {
                        NormalizedTagName = pt.Tag.NormalizedName,
                        TagName = pt.Tag.DisplayName
                    }).ToList()
                }).ToListAsync();

                return new SuccessResponse<IReadOnlyList<PostListItem>>(resultList);
            }, keyParameter: keyword);
        }

        private IQueryable<PostEntity> SearchPostByKeyword(string keyword)
        {
            var query = _postRepository.GetAsQueryable()
                                       .Include(p => p.PostPublish)
                                       .Include(p => p.PostTag)
                                       .ThenInclude(pt => pt.Tag)
                                       .Where(p => !p.PostPublish.IsDeleted && p.PostPublish.IsPublished).AsNoTracking();

            var str = Regex.Replace(keyword, @"\s+", " ");
            var rst = str.Split(' ');
            if (rst.Length > 1)
            {
                // keyword: "dot  net rocks"
                // search for post where Title containing "dot && net && rocks"
                var result = rst.Aggregate(query, (current, s) => current.Where(p => p.Title.Contains(s)));
                return result;
            }
            else
            {
                // keyword: "dotnetrocks"
                var k = rst.First();
                var result = query.Where(p => p.Title.Contains(k) ||
                                              p.PostTag.Select(pt => pt.Tag).Select(t => t.DisplayName).Contains(k));
                return result;
            }
        }

        #endregion

        public string GetPostTitle(Guid postId)
        {
            var spec = new PostSpec(postId, false);
            return _postRepository.SelectFirstOrDefault(spec, p => p.Title);
        }

        public Response<PostEntity> CreateNewPost(CreateEditPostRequest request)
        {
            void ApplyDefaultValuesOnPost(PostEntity postModel)
            {
                if (postModel.Id == Guid.Empty)
                {
                    postModel.Id = Guid.NewGuid();
                }
                if (string.IsNullOrWhiteSpace(postModel.Slug))
                {
                    postModel.Slug = postModel.Id.ToString();
                }

                if (null == postModel.PostExtension)
                {
                    postModel.PostExtension = new PostExtensionEntity
                    {
                        Hits = 0,
                        Likes = 0
                    };
                }
            }

            try
            {
                var postModel = new PostEntity
                {
                    CommentEnabled = request.EnableComment,
                    Id = request.PostId,
                    PostContent = HttpUtility.HtmlEncode(request.HtmlContent),
                    ContentAbstract = Utils.GetPostAbstract(request.HtmlContent, AppSettings.PostSummaryWords),
                    CreateOnUtc = DateTime.UtcNow,
                    Slug = request.Slug.ToLower().Trim(),
                    Title = request.Title.Trim(),
                    PostPublish = new PostPublishEntity
                    {
                        IsDeleted = false,
                        IsPublished = request.IsPublished,
                        PubDateUtc = request.IsPublished ? DateTime.UtcNow : (DateTime?)null,
                        ExposedToSiteMap = request.ExposedToSiteMap,
                        IsFeedIncluded = request.IsFeedIncluded,
                        Revision = 0,
                        ContentLanguageCode = request.ContentLanguageCode
                    }
                };

                // add default values if fields are not assigned
                ApplyDefaultValuesOnPost(postModel);

                // check if exist same slug under the same day
                if (_postRepository.Any(p =>
                    p.Slug == postModel.Slug &&
                    p.PostPublish.PubDateUtc.GetValueOrDefault().Date == DateTime.UtcNow.Date))
                {
                    var uid = Guid.NewGuid();
                    postModel.Slug += $"-{uid.ToString().ToLower().Substring(0, 8)}";
                    Logger.LogInformation($"Found conflict for post slug, generated new slug: {postModel.Slug}");
                }

                // add categories
                if (null != request.CategoryIds && request.CategoryIds.Count > 0)
                {
                    foreach (var cid in request.CategoryIds)
                    {
                        if (_categoryRepository.Any(c => c.Id == cid))
                        {
                            postModel.PostCategory.Add(new PostCategoryEntity
                            {
                                CategoryId = cid,
                                PostId = postModel.Id
                            });
                        }
                    }
                }

                // add tags
                if (null != request.Tags && request.Tags.Count > 0)
                {
                    var tagsList = new List<TagEntity>();
                    foreach (var item in request.Tags)
                    {
                        var tag = _tagRepository.Get(q => q.DisplayName == item);
                        if (null == tag)
                        {
                            // for new tags
                            var newTag = new TagEntity
                            {
                                DisplayName = item,
                                NormalizedName = Utils.NormalizeTagName(item)
                            };

                            tagsList.Add(newTag);
                            _tagRepository.Add(newTag);
                        }
                        else
                        {
                            // existing tags
                            tagsList.Add(tag);
                        }
                    }

                    tagsList.ForEach(t => postModel.PostTag.Add(new PostTagEntity
                    {
                        TagId = t.Id,
                        PostId = postModel.Id
                    }));
                }

                _postRepository.Add(postModel);
                Logger.LogInformation($"New Post Created Successfully. PostId: {postModel.Id}");
                return new SuccessResponse<PostEntity>(postModel);
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error in {nameof(CreateNewPost)}");
                return new FailedResponse<PostEntity>((int)ResponseFailureCode.GeneralException, e.Message);
            }
        }

        public Response<PostEntity> EditPost(CreateEditPostRequest request)
        {
            try
            {
                var postModel = _postRepository.Get(request.PostId);
                if (null == postModel)
                {
                    return new FailedResponse<PostEntity>((int)ResponseFailureCode.PostNotFound);
                }

                postModel.CommentEnabled = request.EnableComment;
                postModel.PostContent = HttpUtility.HtmlEncode(request.HtmlContent);
                postModel.ContentAbstract = Utils.GetPostAbstract(request.HtmlContent, AppSettings.PostSummaryWords);
                postModel.PostPublish.IsPublished = request.IsPublished;
                postModel.Slug = request.Slug;
                postModel.Title = request.Title;
                postModel.PostPublish.ExposedToSiteMap = request.ExposedToSiteMap;
                postModel.PostPublish.LastModifiedUtc = DateTime.UtcNow;
                postModel.PostPublish.IsFeedIncluded = request.IsFeedIncluded;
                postModel.PostPublish.ContentLanguageCode = request.ContentLanguageCode;

                ++postModel.PostPublish.Revision;

                // from draft
                if (!postModel.PostPublish.PubDateUtc.HasValue)
                {
                    postModel.PostPublish.PubDateUtc = DateTime.UtcNow;
                }

                // 1. Add new tags to tag lib
                foreach (var item in request.Tags.Where(item => !_tagRepository.Any(p => p.DisplayName == item)))
                {
                    _tagRepository.Add(new TagEntity
                    {
                        DisplayName = item,
                        NormalizedName = Utils.NormalizeTagName(item)
                    });
                }

                // 2. update tags
                postModel.PostTag.Clear();
                if (request.Tags.Any())
                {
                    foreach (var t in request.Tags)
                    {
                        var tag = _tagRepository.Get(_ => _.DisplayName == t);
                        if (tag != null) postModel.PostTag.Add(new PostTagEntity
                        {
                            PostId = postModel.Id,
                            TagId = tag.Id
                        });
                    }
                }

                // 3. update categories
                postModel.PostCategory.Clear();
                if (null != request.CategoryIds && request.CategoryIds.Count > 0)
                {
                    foreach (var cid in request.CategoryIds)
                    {
                        if (_categoryRepository.Any(c => c.Id == cid))
                        {
                            postModel.PostCategory.Add(new PostCategoryEntity
                            {
                                PostId = postModel.Id,
                                CategoryId = cid
                            });
                        }
                    }
                }

                _postRepository.Update(postModel);
                return new SuccessResponse<PostEntity>(postModel);
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Error {nameof(EditPost)}, PostId: {request.PostId}");
                return new FailedResponse<PostEntity>((int)ResponseFailureCode.GeneralException, e.Message);
            }
        }

        public Response RestoreFromRecycle(Guid postId)
        {
            return TryExecute(() =>
            {
                var pp = _postPublishRepository.Get(postId);
                if (null == pp) return new FailedResponse((int)ResponseFailureCode.PostNotFound);

                pp.IsDeleted = false;
                var rows = _postPublishRepository.Update(pp);
                return new Response(rows > 0);
            }, keyParameter: postId);
        }

        public Response Delete(Guid postId, bool isRecycle = false)
        {
            return TryExecute(() =>
            {
                var post = _postRepository.Get(postId);
                if (null == post) return new FailedResponse((int)ResponseFailureCode.PostNotFound);

                int rows;
                if (isRecycle)
                {
                    post.PostPublish.IsDeleted = true;
                    rows = _postRepository.Update(post);
                }
                else
                {
                    rows = _postRepository.Delete(post);
                }

                return new Response(rows > 0);
            }, keyParameter: postId);
        }

        public async Task<Response> DeleteRecycledPostsAsync()
        {
            return await TryExecuteAsync(async () =>
            {
                var spec = new PostSpec(true);
                var posts = await _postRepository.GetAsync(spec);
                await _postRepository.DeleteAsync(posts);

                return new SuccessResponse();
            });
        }
    }
}