﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using Datory;
using Datory.Utils;
using SiteServer.Abstractions;
using SiteServer.CMS.Core;
using SiteServer.CMS.Framework;
using SqlKata;

namespace SiteServer.CMS.Repositories
{
    public partial class ContentRepository
    {
        public async Task<int> GetMaxTaxisAsync(Site site, Channel channel, bool isTop)
        {
            var repository = await GetRepositoryAsync(site, channel);

            var maxTaxis = 0;
            if (isTop)
            {
                maxTaxis = TaxisIsTopStartValue;

                var max = await repository.MaxAsync(nameof(Content.Taxis),
                    GetQuery(site.Id, channel.Id)
                        .Where(nameof(Content.Taxis), ">", TaxisIsTopStartValue)
                );
                if (max.HasValue)
                {
                    maxTaxis = max.Value;
                }

                if (maxTaxis < TaxisIsTopStartValue)
                {
                    maxTaxis = TaxisIsTopStartValue;
                }
            }
            else
            {
                var max = await repository.MaxAsync(nameof(Content.Taxis),
                    GetQuery(site.Id, channel.Id)
                    .Where(nameof(Content.Taxis), "<", TaxisIsTopStartValue)
                );
                if (max.HasValue)
                {
                    maxTaxis = max.Value;
                }
            }
            return maxTaxis;
        }

        private async Task<List<ContentSummary>> GetReferenceIdListAsync(string tableName, IEnumerable<int> contentIdList)
        {
            var repository = GetRepository(tableName);
            return await repository.GetAllAsync<ContentSummary>(Q
                .Select(ContentAttribute.Id, ContentAttribute.ChannelId)
                .Where(ContentAttribute.ChannelId, ">", 0)
                .WhereIn(ContentAttribute.ReferenceId, contentIdList)
            );
        }

        public async Task<int> GetFirstContentIdAsync(string tableName, int channelId)
        {
            var repository = GetRepository(tableName);
            return await repository.GetAsync<int>(Q
                .Select(ContentAttribute.Id)
                .Where(nameof(Content.ChannelId), channelId)
                .OrderByDesc(ContentAttribute.Taxis, ContentAttribute.Id)
            );
        }

        public List<(string userName, int AddCount, int UpdateCount)> GetDataSetOfAdminExcludeRecycle(string tableName, int siteId, DateTime begin, DateTime end)
        {
            var sqlString = $@"select userName,SUM(addCount) as addCount, SUM(updateCount) as updateCount from( 
SELECT AddUserName as userName, Count(AddUserName) as addCount, 0 as updateCount FROM {tableName} 
INNER JOIN {_administratorRepository.TableName} ON AddUserName = {_administratorRepository.TableName}.UserName 
WHERE {tableName}.SiteId = {siteId} AND (({tableName}.ChannelId > 0)) 
AND LastEditDate BETWEEN {SqlUtils.GetComparableDate(begin)} AND {SqlUtils.GetComparableDate(end.AddDays(1))}
GROUP BY AddUserName
Union
SELECT LastEditUserName as userName,0 as addCount, Count(LastEditUserName) as updateCount FROM {tableName} 
INNER JOIN {_administratorRepository.TableName} ON LastEditUserName = {_administratorRepository.TableName}.UserName 
WHERE {tableName}.SiteId = {siteId} AND (({tableName}.ChannelId > 0)) 
AND LastEditDate BETWEEN {SqlUtils.GetComparableDate(begin)} AND {SqlUtils.GetComparableDate(end.AddDays(1))}
AND LastEditDate != AddDate
GROUP BY LastEditUserName
) as tmp
group by tmp.userName";

            var list = new List<(string UserName, int AddCount, int UpdateCount)>();

            var reposotory = GetRepository(tableName);
            using (var connection = reposotory.Database.GetConnection())
            {
                using (var rdr = connection.ExecuteReader(sqlString))
                {
                    while (rdr.Read())
                    {
                        var userName = rdr.IsDBNull(0) ? string.Empty : rdr.GetString(0);
                        var addCount = rdr.IsDBNull(1) ? 0 : rdr.GetInt32(1);
                        var updateCount = rdr.IsDBNull(2) ? 0 : rdr.GetInt32(2);

                        if (!string.IsNullOrEmpty(userName))
                        {
                            list.Add((userName, addCount, updateCount));
                        }
                    }
                }
            }

            return list;
        }

        public async Task<int> GetCountOfContentUpdateAsync(string tableName, int siteId, int channelId, ScopeType scope, DateTime begin, DateTime end, int adminId)
        {
            var channelIdList = await _channelRepository.GetChannelIdsAsync(siteId, channelId, scope);
            return await GetCountOfContentUpdateAsync(tableName, siteId, channelIdList, begin, end, adminId);
        }

        private async Task<int> GetCountOfContentUpdateAsync(string tableName, int siteId, List<int> channelIdList, DateTime begin, DateTime end, int adminId)
        {
            var repository = GetRepository(tableName);
            var query = Q.Where(nameof(Content.SiteId), siteId);
            query.WhereIn(nameof(Content.ChannelId), channelIdList);
            query.WhereBetween(nameof(Content.LastModifiedDate), begin, end.AddDays(1));
            query.WhereRaw($"{nameof(Content.LastModifiedDate)} != {nameof(Content.AddDate)}");
            if (adminId > 0)
            {
                query.Where(nameof(Content.AdminId), adminId);
            }

            return await repository.CountAsync(query);
        }

        public async Task<List<int>> GetIdListBySameTitleAsync(Site site, Channel channel, string title)
        {
            var repository = await GetRepositoryAsync(site, channel);

            return await repository.GetAllAsync<int>(GetQuery(site.Id, channel.Id)
                .Select(nameof(Content.Id))
                .Where(nameof(Content.Title), title)
            );
        }

        public async Task<int> GetCountOfContentAddAsync(string tableName, int siteId, int channelId, ScopeType scope, DateTime begin, DateTime end, int adminId, bool? checkedState)
        {
            var channelIdList = await _channelRepository.GetChannelIdsAsync(siteId, channelId, scope);
            return await GetCountOfContentAddAsync(tableName, siteId, channelIdList, begin, end, adminId, checkedState);
        }

        private async Task<int> GetCountOfContentAddAsync(string tableName, int siteId, List<int> channelIdList, DateTime begin, DateTime end, int adminId, bool? checkedState)
        {
            var repository = GetRepository(tableName);

            var query = Q.Where(nameof(Content.SiteId), siteId);
            query.WhereIn(nameof(Content.ChannelId), channelIdList);
            query.WhereBetween(nameof(Content.AddDate), begin, end.AddDays(1));
            if (adminId > 0)
            {
                query.Where(nameof(Content.AdminId), adminId);
            }

            if (checkedState.HasValue)
            {
                query.Where(nameof(Content.Checked), TranslateUtils.ToBool(checkedState.ToString()));
            }

            return await repository.CountAsync(query);
        }

        public async Task<List<ContentSummary>> GetSummariesAsync(string tableName, Query query)
        {
            var repository = GetRepository(tableName);
            return await repository.GetAllAsync<ContentSummary>(query);
        }

        public async Task<int> GetCountAsync(string tableName, Query query)
        {
            var repository = GetRepository(tableName);
            return await repository.CountAsync(query);
        }

        public async Task<string> GetWhereStringByStlSearchAsync(bool isAllSites, string siteName, string siteDir, string siteIds, string channelIndex, string channelName, string channelIds, string type, string word, string dateAttribute, string dateFrom, string dateTo, string since, int siteId, List<string> excludeAttributes, NameValueCollection form)
        {
            var whereBuilder = new StringBuilder();

            Site site = null;
            if (!string.IsNullOrEmpty(siteName))
            {
                site = await _siteRepository.GetSiteBySiteNameAsync(siteName);
            }
            else if (!string.IsNullOrEmpty(siteDir))
            {
                site = await _siteRepository.GetSiteByDirectoryAsync(siteDir);
            }
            if (site == null)
            {
                site = await _siteRepository.GetAsync(siteId);
            }

            var channelId = await _channelRepository.GetChannelIdAsync(siteId, siteId, channelIndex, channelName);
            var channelInfo = await _channelRepository.GetAsync(channelId);

            if (isAllSites)
            {
                whereBuilder.Append("(SiteId > 0) ");
            }
            else if (!string.IsNullOrEmpty(siteIds))
            {
                whereBuilder.Append($"(SiteId IN ({TranslateUtils.ToSqlInStringWithoutQuote(Utilities.GetIntList(siteIds))})) ");
            }
            else
            {
                whereBuilder.Append($"(SiteId = {site.Id}) ");
            }

            if (!string.IsNullOrEmpty(channelIds))
            {
                whereBuilder.Append(" AND ");
                var channelIdList = new List<int>();
                foreach (var theChannelId in Utilities.GetIntList(channelIds))
                {
                    var theChannel = await _channelRepository.GetAsync(theChannelId);
                    channelIdList.AddRange(
                        await _channelRepository.GetChannelIdsAsync(theChannel.SiteId, theChannel.Id, ScopeType.All));
                }
                whereBuilder.Append(channelIdList.Count == 1
                    ? $"(ChannelId = {channelIdList[0]}) "
                    : $"(ChannelId IN ({TranslateUtils.ToSqlInStringWithoutQuote(channelIdList)})) ");
            }
            else if (channelId != siteId)
            {
                whereBuilder.Append(" AND ");

                var channelIdList = await _channelRepository.GetChannelIdsAsync(siteId, channelId, ScopeType.All);

                whereBuilder.Append(channelIdList.Count == 1
                    ? $"(ChannelId = {channelIdList[0]}) "
                    : $"(ChannelId IN ({TranslateUtils.ToSqlInStringWithoutQuote(channelIdList)})) ");
            }

            var typeList = new List<string>();
            if (string.IsNullOrEmpty(type))
            {
                typeList.Add(ContentAttribute.Title);
            }
            else
            {
                typeList = Utilities.GetStringList(type);
            }

            if (!string.IsNullOrEmpty(word))
            {
                whereBuilder.Append(" AND (");
                foreach (var attributeName in typeList)
                {
                    whereBuilder.Append($"[{attributeName}] LIKE '%{AttackUtils.FilterSql(word)}%' OR ");
                }
                whereBuilder.Length = whereBuilder.Length - 3;
                whereBuilder.Append(")");
            }

            if (string.IsNullOrEmpty(dateAttribute))
            {
                dateAttribute = ContentAttribute.AddDate;
            }

            if (!string.IsNullOrEmpty(dateFrom))
            {
                whereBuilder.Append(" AND ");
                whereBuilder.Append($" {dateAttribute} >= {SqlUtils.GetComparableDate(TranslateUtils.ToDateTime(dateFrom))} ");
            }
            if (!string.IsNullOrEmpty(dateTo))
            {
                whereBuilder.Append(" AND ");
                whereBuilder.Append($" {dateAttribute} <= {SqlUtils.GetComparableDate(TranslateUtils.ToDateTime(dateTo))} ");
            }
            if (!string.IsNullOrEmpty(since))
            {
                var sinceDate = DateTime.Now.AddHours(-DateUtils.GetSinceHours(since));
                whereBuilder.Append($" AND {dateAttribute} BETWEEN {SqlUtils.GetComparableDateTime(sinceDate)} AND {SqlUtils.GetComparableNow()} ");
            }

            var tableName = await _channelRepository.GetTableNameAsync(site, channelInfo);
            //var styleInfoList = RelatedIdentities.GetTableStyleInfoList(site, channel.Id);

            foreach (string key in form.Keys)
            {
                if (excludeAttributes.Contains(key.ToLower())) continue;
                if (string.IsNullOrEmpty(form[key])) continue;

                var value = StringUtils.Trim(form[key]);
                if (string.IsNullOrEmpty(value)) continue;

                var columnInfo = await TableColumnManager.GetTableColumnInfoAsync(tableName, key);

                if (columnInfo != null && (columnInfo.DataType == DataType.VarChar || columnInfo.DataType == DataType.Text))
                {
                    whereBuilder.Append(" AND ");
                    whereBuilder.Append($"({key} LIKE '%{value}%')");
                }
                //else
                //{
                //    foreach (var tableStyleInfo in styleInfoList)
                //    {
                //        if (StringUtils.EqualsIgnoreCase(tableStyleInfo.AttributeName, key))
                //        {
                //            whereBuilder.Append(" AND ");
                //            whereBuilder.Append($"({ContentAttribute.SettingsXml} LIKE '%{key}={value}%')");
                //            break;
                //        }
                //    }
                //}
            }

            return whereBuilder.ToString();
        }

        public async Task CreateContentTableAsync(string tableName, List<TableColumn> columnInfoList)
        {
            var isDbExists = await WebConfigUtils.Database.IsTableExistsAsync(tableName);
            if (isDbExists) return;

            await WebConfigUtils.Database.CreateTableAsync(tableName, columnInfoList);
            await WebConfigUtils.Database.CreateIndexAsync(tableName, $"IX_{tableName}",
                $"{nameof(Content.Top)} DESC", $"{ContentAttribute.Taxis} DESC", $"{ContentAttribute.Id} DESC");
            await WebConfigUtils.Database.CreateIndexAsync(tableName, $"IX_{tableName}_Taxis",
                $"{ContentAttribute.Taxis} DESC");
        }

        private async Task QueryWhereAsync(Query query, Site site, int channelId, bool isAllContents)
        {
            query.Where(nameof(Content.SiteId), site.Id);
            query.WhereNot(nameof(Content.SourceId), SourceManager.Preview);

            if (isAllContents)
            {
                var channelIdList = await _channelRepository.GetChannelIdsAsync(site.Id, channelId, ScopeType.All);
                query.WhereIn(nameof(Content.ChannelId), channelIdList);
            }
            else
            {
                query.Where(nameof(Content.ChannelId), channelId);
            }
        }
    }
}