using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ImPluginEngine.Abstractions.Entities;
using ImPluginEngine.Abstractions.Helpers;
using ImPluginEngine.Abstractions.Interfaces;
using Newtonsoft.Json;

namespace SMChartsPlugin
{
    public class SMChartsPlugin : IPlugin, ITrackInformationPlugin
    {
        public string Name => "SMCharts plugin";
        public string Version => "0.2.2";

        public async Task GetTrackInformationSearch(string title, string artist, string extraData, int page, ITrackCallback callback)
        {
            PluginTrackSearchResult release = new PluginTrackSearchResult();

            release.Details = string.Format("{0} v{1}", Name, Version);
            release.Format = "charts";
            release.Id = "1";
            release.IsCompleted = false;
            release.Title = string.Format("Generic: {0} - {1}", artist, title);
            callback.TrackUpdateCallback(release);

            // send a complete message...
            callback.TrackUpdateCallback(new PluginTrackSearchResult()
            {
                IsCompleted = true
            });
        }
        public async Task<PluginAlbumInformation> GetTrackInformationResult(string id, string data, CancellationToken ct)
        {
            PluginAlbumInformation result = new PluginAlbumInformation();
            List<PluginTrackPayload> trackInfoList = JsonConvert.DeserializeObject<List<PluginTrackPayload>>(data);
            int trackCount = trackInfoList.Count();
            result.Id = 1;
            result.IsEmpty = false;
            foreach (PluginTrackPayload trackInfo in trackInfoList)
            {
                PluginTrackInformation track = await GetTrack(trackInfo, trackCount, ct);
                result.TrackList.Add(track);
            }
// return the album info...
            return result;
        }

// private functions
        private async Task<PluginTrackInformation> GetTrack(PluginTrackPayload trackInfo, int maxTracks, CancellationToken ct)
        {
            PluginTrackInformation track = new PluginTrackInformation();
            string ver = "2.0"; // this is the version of downloaded information
            List<string> excludemultiartists = LoadExcludeArtists();
            List<SMMultiArtistCommands> multiartistcommands = LoadMultiArtistCommands();
            string id = await GetId(trackInfo.Artist, trackInfo.Title, trackInfo.Subtitle, trackInfo.Remix);
            string web = await LoadWebpage(string.Format("https://www.offiziellecharts.de/titel-details-{0}", id));
            track.Artist = GetTrackArtist(web);
            List<SMRelatedArtist> relatedartists = GetRelatedArtists(web, track.Artist);
            track.Artist = CreateTrackArtist(track.Artist, relatedartists);
            if (AddNewExcludeArtists(relatedartists, multiartistcommands, ref excludemultiartists))
                SaveExcludeArtists(excludemultiartists);
            if (string.IsNullOrEmpty(track.Artist))
            {
                track.Artist = trackInfo.Artist;
                track.Title = trackInfo.Title;
            }
            else
            {
                track.MultipleArtists = CreateMultipleArtists(track.Artist, multiartistcommands, excludemultiartists);
                track.Artist = CreateArtistFromMultipleArtists(track.MultipleArtists);
                track.ArtistSortOrder = CreateArtistFromMultipleArtists(track.MultipleArtists, true);
                SMTrackTitle tracktitle = GetTrackTitle(web);
                track.Title = tracktitle.Title;
                track.Subtitle = tracktitle.Subtitle;
                track.Remix = tracktitle.Remix;
                track.TitleSortOrder = CreateSortString(track.Title);
                track.Custom1 = GetTrackChartsPeak(web);
                track.Custom2 = GetTrackChartsWeeks(web);
                track.Custom3 = GetTrackChartsPoints(web);
                if (string.IsNullOrEmpty(track.Custom3) && track.Custom2 == "001")
                {
                    int points = (101 - Int32.Parse(track.Custom1));
                    track.Custom3 = points.ToString().PadLeft(5, '0');
                }
                track.RecordingYear = GetTrackRecYear(web);
                web = await LoadWebpage(string.Format("https://hitparade.ch/song/song/song-{0}", id));
                if (string.IsNullOrEmpty(track.Custom1))
                {
                    track.Custom1 = GetTrackChartsPeakAgain(web);
                    track.Custom2 = GetTrackChartsWeeksAgain(web);
                    track.Custom3 = GetTrackChartsPointsEstimated(track.Custom1, track.Custom2);
                }
                track.Custom10 = string.Format("{0}|{1}", GetTrackRating(web).ToString(), GetTrackPopularity(track.Custom3).ToString());
                if (track.Custom10 == "0|255")
                {
                    track.Custom10 = string.Format("Charts:{0}", ver);
                }
                else
                {
                    track.Custom10 = string.Format("Charts:{0}|{1}", ver, track.Custom10);
                }
            }
            return track;
        }
        private string GetTrackArtist(string web)
        {
            Regex regex = new Regex(@"<h1 class=""big-head"">(?'match'[^<]+)</h1>", RegexOptions.Compiled);
            Match match = regex.Match(web);
            if (match.Success)
            {
                return WebUtility.HtmlDecode(match.Groups["match"].Value.Trim());
            }
            return string.Empty;
        }
        private string CreateTrackArtist(string artist, List<SMRelatedArtist> relatedartists)
        {
            foreach (SMRelatedArtist relatedartist in relatedartists)
            {
                if (artist.Contains(relatedartist.commonname)) artist.Replace(relatedartist.commonname, relatedartist.uniquename);
            }
            return artist;
        }
        private List<SMRelatedArtist> GetRelatedArtists(string web, string artist)
        {
            Regex regex = new Regex(@"<b>\d+\s+Titel</b>\s+von\s+<span>(?'uniquename'[^<]+)</span>", RegexOptions.Compiled);
            MatchCollection matches = regex.Matches(web);
            List<SMRelatedArtist> listres = new List<SMRelatedArtist>();
            if (matches.Count > 0)
            {
                int i = 0;
                foreach (Match match in matches)
                {
                    if (i > 0 || matches.Count == 1)
                    {
                        SMRelatedArtist res = new SMRelatedArtist();
                        res.uniquename = match.Groups["uniquename"].Value.Trim();
                        if (res.uniquename.EndsWith("]")) res.commonname = res.uniquename.Substring(0, res.uniquename.IndexOf("[") - 1);
                        else res.commonname = res.uniquename;
                        listres.Add(res);
                    }
                    i++;
                }
            }
            return listres;
        }
        private SMTrackTitle GetTrackTitle(string web)
        {
            SMTrackTitle tracktitle = new SMTrackTitle();
            Regex regex = new Regex(@"<h2 class=""sub-head"">(?'match'[^<]+)<", RegexOptions.Compiled);
            Match match = regex.Match(web);
            if (match.Success)
            {
                string title =  WebUtility.HtmlDecode(match.Groups["match"].Value.Trim());
                string[] remixes = { "version", "remix", "rmx", "edit", "unplugged", "mix", "demo", "outtake", "remaster", "explicit", "7\"", "12\"", "b-side", "original", "capella", "session", "acoustic", "cut", "akustik", "dub", "akustisch", "clean", "altered", "alternate", "explicit" };
                if (title.EndsWith("]"))
                {
                    tracktitle.Remix = title.Substring(title.LastIndexOf('[') + 1, title.Length - title.LastIndexOf('[') - 2).Trim();
                    title = title.Substring(0, title.LastIndexOf('[')).Trim();
                }
                while (title.EndsWith(")"))
                {
                    string test = title.Substring(title.LastIndexOf('(') + 1, title.LastIndexOf(')') - title.LastIndexOf('(') - 1).Trim();
                    title = title.Substring(0, title.LastIndexOf('(')).Trim();
                    bool found = false;
                    if (!string.IsNullOrEmpty(tracktitle.Remix))
                    {
                        tracktitle.Subtitle = test;
                        found = true;
                    }
                    if (!found)
                    {
                        foreach (string remix in remixes)
                        {
                            if (test.ToLower().Contains(remix))
                            {
                                tracktitle.Remix = test;
                                found = true;
                                break;
                            }
                        }
                    }
                    if (!found)
                    {
                        tracktitle.Subtitle = test;
                    }
                }
                tracktitle.Title = title;
            }
            return tracktitle;
        }
        private string GetTrackChartsPeak(string web)
        {
            Regex regex = new Regex(@"Höchstposition:\s*</td>\s*<td>(?'match'[\d]+)\s*\([\d]+\s*Wochen\)\s*</td>", RegexOptions.Compiled);
            Match match = regex.Match(web);
            if (match.Success)
            {
                return match.Groups["match"].Value.PadLeft(3, '0');
            }
            return string.Empty;
        }
        private string GetTrackChartsPeakAgain(string web)
        {
            Regex regex = new Regex(@"<img src=""/images/de.gif""[^>]*></a>\s*Peak:\s*(?'match'[\d]+)\s*/\s*Wochen:\s*[\d]+</td>", RegexOptions.Compiled);
            Match match = regex.Match(web);
            if (match.Success)
            {
                return match.Groups["match"].Value.PadLeft(3, '0');
            }
            return string.Empty;
        }
        private string GetTrackChartsPoints(string web)
        {
            int points = 0;
            Regex regex = new Regex(@"data-chart=""(?'match'[^""]+)""", RegexOptions.Compiled);
            Match match = regex.Match(web);
            if (match.Success)
            {
                string divider = ",";
                string[] div = { divider };
                string values = match.Groups["match"].Value.Replace(",null", "");
                string[] positions = values.Split(div, StringSplitOptions.RemoveEmptyEntries).ToArray();
                foreach (string pos in positions)
                {
                    points = points + (101 - Int32.Parse(pos));
                }
                return points.ToString().PadLeft(5, '0');
            }
            return string.Empty;
        }
        private string GetTrackChartsPointsEstimated(string peak, string weeks)
        {
            if (Int32.TryParse(peak, out int p) && Int32.TryParse(weeks, out int w))
            {
                int points = 0;
                if (w == 1)
                {
                    points = (101 - p);
                }
                else
                {
                    points = Convert.ToInt32(Math.Floor(((101 - p) * w) * 0.6666667));
                }
                return points.ToString().PadLeft(5, '0');
            }
            return string.Empty;
        }
        private string GetTrackChartsWeeks(string web)
        {
            Regex regex = new Regex(@"Anzahl Wochen:\s*</td>\s*<td>(?'match'[\d]+)\s*</td>", RegexOptions.Compiled);
            Match match = regex.Match(web);
            if (match.Success)
            {
                return match.Groups["match"].Value.PadLeft(3, '0');
            }
            return string.Empty;
        }
        private string GetTrackChartsWeeksAgain(string web)
        {
            Regex regex = new Regex(@"<img src=""/images/de.gif""[^>]*></a>\s*Peak:\s*[\d]+\s*/\s*Wochen:\s*(?'match'[\d]+)</td>", RegexOptions.Compiled);
            Match match = regex.Match(web);
            if (match.Success)
            {
                return match.Groups["match"].Value.PadLeft(3, '0');
            }
            return string.Empty;
        }
        private int GetTrackPopularity(string points)
        {
            if (Int32.TryParse(points, out int pop))
            {
                if (pop >= 2400) return 25;
                if (pop >= 1500) return 51;
                if (pop >= 900) return 76;
                if (pop >= 550) return 102;
                if (pop >= 350) return 127;
                if (pop >= 200) return 153;
                if (pop >= 125) return 178;
                if (pop >= 75) return 204;
                if (pop >= 1) return 229;
            }
            return 255;
        }
        private int GetTrackRating(string web)
        {
            Regex regex = new Regex(@"<b>(?'match'[\d.]+)<div", RegexOptions.Compiled);
            Match match = regex.Match(web);
            if (match.Success)
            {
                if (Double.TryParse(match.Groups["match"].Value, NumberStyles.Number, CultureInfo.CreateSpecificCulture("en-US"), out double rating))
                {
                    if (rating >= 5) return 255;
                    if (rating > 0) return Convert.ToInt32(Math.Floor(Math.Floor(rating * 2) * 25.5));
                }
            }
            return 0;
        }
        private int GetTrackRecYear(string web)
        {
            Regex regex = new Regex(@"Jahr\s*:\s*</td>\s*<td>(?'match'[\d]+)\s*</td>", RegexOptions.Compiled);
            Match match = regex.Match(web);
            if (match.Success)
            {
                return Int32.Parse(match.Groups["match"].Value);
            }
            return -1;
        }
        private async Task<string> GetId(string artist, string title, string subtitle, string remix)
        {
            string id = string.Empty;
            string a = CleanString(artist.ToLower());
            string ca = a;
            while (ca.Contains("["))
            {
                ca = ca.Remove(ca.IndexOf("[") - 1, ca.IndexOf("]") - ca.IndexOf("[") + 2);
            }
            string t = CleanString(title.ToLower());
            string search = CreateSearchString(ca, t);
            string ts = t;
            if (!string.IsNullOrEmpty(subtitle))
            {
                ts += " (" + subtitle.ToLower() + ")";
            }
            string tr = t;
            string trb = t;
            string tsr = ts;
            string tsrb = ts;
            if (!string.IsNullOrEmpty(remix))
            {
                tr += " " + remix.ToLower();
                trb += " [" + remix.ToLower() + "]";
                tsr += " " + remix.ToLower();
                tsrb += " [" + remix.ToLower() + "]";
                if (string.IsNullOrEmpty(subtitle))
                {
                    ts = t + " (" + remix + ")";
                }
            }
            string url = string.Format(@"https://hitparade.ch/search.asp?cat=s&search={0}", search);
            string web = await LoadWebpage(url);
            if (!string.IsNullOrEmpty(subtitle) || !string.IsNullOrEmpty(remix))
            {
                id = SearchID(a, tsrb, web);
                if (id == "---")
                {
                    search = CreateSearchString(a, t, false);
                    url = string.Format(@"https://hitparade.ch/search.asp?cat=s&search={0}", search);
                    web = await LoadWebpage(url);
                    id = SearchID(a, tsrb, web);
                    if (id == "---") return string.Empty;
                }
                if (!string.IsNullOrEmpty(id)) return id;
                id = SearchID(a, tsr, web);
                if (!string.IsNullOrEmpty(id)) return id;
                if (!string.IsNullOrEmpty(subtitle))
                {
                    id = SearchID(a, trb, web);
                    if (!string.IsNullOrEmpty(id)) return id;
                    id = SearchID(a, tr, web);
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                if (string.IsNullOrEmpty(subtitle) && !string.IsNullOrEmpty(remix))
                {
                    id = SearchID(a, ts, web);
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                id = SearchID(a, tsrb, web, false);
                if (!string.IsNullOrEmpty(id)) return id;
                id = SearchID(a, tsr, web, false);
                if (!string.IsNullOrEmpty(id)) return id;
                if (!string.IsNullOrEmpty(subtitle))
                {
                    id = SearchID(a, trb, web, false);
                    if (!string.IsNullOrEmpty(id)) return id;
                    id = SearchID(a, tr, web, false);
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                if (string.IsNullOrEmpty(subtitle) && !string.IsNullOrEmpty(remix))
                {
                    id = SearchID(a, ts, web, false);
                    if (!string.IsNullOrEmpty(id)) return id;
                }
            }
            id = SearchID(a, t, web);
            if (!string.IsNullOrEmpty(id)) return id;
            id = SearchID(a, t, web, false);
            if (!string.IsNullOrEmpty(id)) return id;
            return id;
        }
        private string SearchID(string a, string t, string web, bool directcompair = true)
        {
            Regex regex = new Regex(@"<td class=""text"">\s*<a href=""(?'url'[^""]+)"">(?'artist'[^<]+)</a>\s*</td>\s*<td class=""text"">\s*<a href=""[^""]+"">(?'title'[^<]+)</a>", RegexOptions.Compiled);
            MatchCollection matches = regex.Matches(web);
            string id = string.Empty;
            if (directcompair)
            {
                if (matches.Count == 0) return "---";
                foreach (Match m in matches)
                {
                    string ac = WebUtility.HtmlDecode(m.Groups["artist"].Value);
                    string tc = WebUtility.HtmlDecode(m.Groups["title"].Value);
                    if (a == ac.ToLower() && t == tc.ToLower())
                    {
                        id = m.Groups["url"].Value;
                        break;
                    }
                }
            }
            else
            {
                if (matches.Count == 0) return "---";
                foreach (Match m in matches)
                {
                    string ac = WebUtility.HtmlDecode(m.Groups["artist"].Value);
                    string tc = WebUtility.HtmlDecode(m.Groups["title"].Value);
                    if ((a.Contains(ac.ToLower()) || ac.ToLower().Contains(a)) && (t.Contains(tc.ToLower()) || tc.ToLower().Contains(t)))
                    {
                        id = m.Groups["url"].Value;
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(id)) return id.Substring(id.LastIndexOf('-') + 1);
            return string.Empty;
        }
        private string CreateSearchString(string a, string t, bool myencode = true)
        {
            string qa = a.Replace(" and ", " ").Replace(" und ", " ").Replace(" et ", " ").Replace(" with ", " ").Replace(" mit ", " ").Replace(" avec ", " ").Replace(" feat. ", " ");
            qa = qa.Replace(" vs. ", " ").Replace(" pres. ", " ").Replace(" meets ", " ").Replace(" joins ", " ").Replace(" battles ", " ").Replace(" aka ", " ").Replace(" + ", " ");
            qa = qa.Replace(" con ", " ").Replace(" x ", " ").Replace(" battles ", " ").Replace(" remixed by ", " ");
            qa = qa.Replace("&", " ").Replace("(", " ").Replace(")", " ").Replace("! ", " ").Replace("?", " ").Replace(",", " ").Replace(" + ", " ").Replace("/", " ").Replace("Λ", " ");
            qa = qa.Replace(@"\", " ").Replace(@"""", " ").Replace(". ", " ").Replace("-", " ").Replace(": ", " ").Replace("; ", " ").Replace("...", " ").Replace("…", " ").Trim();
            string qt = t.Replace("&", " ").Replace("(", " ").Replace(")", " ").Replace("! ", " ").Replace("?", " ").Replace(",", " ").Replace(" + ", " ").Replace("/", " ").Replace("Λ", " ");
            qt = qt.Replace(@"\", " ").Replace(@"""", " ").Replace(". ", " ").Replace("-", " ").Replace(": ", " ").Replace("; ", " ").Replace("...", " ").Replace("…", " ").Trim();
            qa = RemoveShortWords(qa);
            if (string.IsNullOrEmpty(qa)) qa = a;
            qt = RemoveShortWords(qt);
            if (string.IsNullOrEmpty(qt)) qt = t;
            string q = qa + " " + qt;
            while (q.Contains("  "))
            {
                q = q.Replace("  ", " ");
            }
            q = RemoveDuplicates(q, " ", false, true);
            if (q.Length > 45)
            {
                q = q.Substring(0, 45);
                q = q.Substring(0, q.LastIndexOf(" "));
            }
            if (myencode)
            {
                Encoding isoenc = Encoding.GetEncoding("ISO-8859-1");
                return MyUrlEncode(q, isoenc);
            }
            return WebUtility.HtmlEncode(q);
        }
        private static string CleanString(String s)
        {
            s = s.Replace("´", "'").Replace("`", "'").Replace("’", "'").Replace("‘", "'");
            s = s.Replace("…", "...").Replace("‐", "-");
            return s.Trim();
        }
        private string GetSearchUrl(int index)
        {
            string query = "/search.asp?cat={0}&artist={1}&artist_search=starts&title={2}&title_search=starts";
            return string.Format("{0}{1}", GetMainUrl(index), query);
        }
        private string GetMainUrl(int index)
        {
            return "https://hitparade.ch";
        }
        private string GetAlbumRegex(int index)
        {
            return @"<td class=""text"">\s*<a href=""(?'url'[^""]+)"">(?'artist'[^<]+)</a>\s*</td>\s*<td class=""text"">\s*<a href=[^>]+>(?'title'[^<]+)</a>";
        }
        private string GetReleaseRegex(int index)
        {
            return @"(?'details'<b>[\d.]+</b>.+</td>)<td style=""border-top:1px #FFFFFF solid;"">";
        }
        private async Task<List<SMChartsAlbum>> GetAlbums(string url, SMChartsSettings s)
        {
            int id = 0;
            List<SMChartsAlbum> results = new List<SMChartsAlbum>();
            string web = await LoadWebpage(url);
            Regex AlbumRegex = new Regex(GetAlbumRegex(s.CountryIndex), RegexOptions.Compiled);
            MatchCollection matches = AlbumRegex.Matches(web);
            foreach (Match match in matches)
            {
                SMChartsAlbum album = new SMChartsAlbum();
                album.Artist = match.Groups["artist"].Value;
                album.Title = match.Groups["album"].Value;
                album.Url = match.Groups["url"].Value;
                album.Releases = await GetReleases(album.Title, album.Artist, album.Url, ++id, s);
                results.Add(album);
            }
            return results;
        }
        private async Task<List<PluginTrackSearchResult>> GetReleases(string album, string artist, string url, int id, SMChartsSettings s)
        {
            int id2 = 0;
            List<PluginTrackSearchResult> results = new List<PluginTrackSearchResult>();
            string web = await LoadWebpage(string.Format("{0}{1}", GetMainUrl(s.CountryIndex), url));
            web = PrepareWebpage(web);
            Regex AlbumRegex = new Regex(GetReleaseRegex(s.CountryIndex), RegexOptions.Compiled);
            MatchCollection matches = AlbumRegex.Matches(web);
            foreach (Match match in matches)
            {
                id2++;
                PluginTrackSearchResult result = new PluginTrackSearchResult();
                result.CatalogNumber = GetCatalogNumber(match.Groups["details"].Value);
                result.Format = GetFormat(match.Groups["details"].Value);
                result.Id = string.Format("{0}-{1}", id.ToString(), id2.ToString());
                result.Label = GetLabel(match.Groups["details"].Value);
                result.Title = string.Format("{0} - {1}", artist, album);
                result.Year = GetYear(match.Groups["details"].Value);
            }
            return results;
        }
        private string GetCatalogNumber(string web)
        {
            string res = string.Empty;
            Regex regex = new Regex(@""">\*(?'catno'[^\s]+)\s+[^(]+[^/]+/\s+EAN[\s\d]+</span>", RegexOptions.Compiled);
            MatchCollection matches = regex.Matches(web);
            foreach (Match match in matches)
                res = AddItem(res, match.Groups["catno"].Value.Replace("A|B</a> ", ""));
            return RemoveDuplicates(res);
        }
        private string GetFormat(string web)
        {
            string res = string.Empty;
            Regex regex = new Regex(@"</div><b>(?'format'[^<]+)</b>", RegexOptions.Compiled);
            MatchCollection matches = regex.Matches(web);
            foreach (Match match in matches)
                res = AddItem(res, match.Groups["format"].Value);
            return RemoveDuplicates(res);
        }
        private string GetLabel(string web)
        {
            string res = string.Empty;
            Regex regex = new Regex(@""">\*[^\s]+\s+(?'label'[^(]+)[^/]+/\s+EAN[\s\d]+</span>", RegexOptions.Compiled);
            MatchCollection matches = regex.Matches(web);
            foreach (Match match in matches)
                res = AddItem(res, match.Groups["label"].Value);
            return RemoveDuplicates(res);
        }
        private string GetYear(string web)
        {
            string res = string.Empty;
            Regex regex = new Regex(@"<b>(?'year'[\d.]+)</b>", RegexOptions.Compiled);
            MatchCollection matches = regex.Matches(web);
            foreach (Match match in matches)
                res = AddItem(res, match.Groups["format"].Value);
            return RemoveDuplicates(res);
        }
        private async Task<string> LoadWebpage(string url)
        {
            var client = new HttpClient();
            string web = string.Empty;
            try
            {
                var response = await client.GetAsync(url);
                web = await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException)
            {
                return string.Empty;
            }
            return web;
        }
        private string PrepareWebpage(string web)
        {
            web = Regex.Replace(web, @"\s*<script>hasAudio\s*=\s*false;</script>\s*", "");
            web = Regex.Replace(web, @"\s*<script>hasAudio\s*=\s*true;</script>\s*", "");
            return Regex.Replace(web, @"<div\s*style\s*=\s*""float:right;margin-left:6px;"">", "\r\n");
        }

// general functions
        private string AddItem(string a, string b, string divider = "; ")
        {
            if (!string.IsNullOrEmpty(a))
                a += divider;
            return a += b.Trim();
        }
        private bool AddNewExcludeArtists(List<SMRelatedArtist> relatedartists, List<SMMultiArtistCommands> multiartistcommands, ref List<string> excludemultiartists)
        {
            bool found = false;
            foreach (SMRelatedArtist artist in relatedartists)
            {
                if (!excludemultiartists.Contains(artist.uniquename))
                {
                    if (artist.uniquename.Contains(", "))
                    {
                        excludemultiartists.Add(artist.uniquename);
                        found = true;
                    }
                    else
                    {
                        foreach (SMMultiArtistCommands command in multiartistcommands)
                        {
                            if (artist.uniquename.Contains(" " + command.Original + " "))
                            {
                                excludemultiartists.Add(artist.uniquename);
                                found = true;
                                break;
                            }
                        }
                    }
                }
            }
            if (found)
                excludemultiartists.Sort();
            return found;
        }
        private string RemoveDuplicates(string list, string divider = "; ", bool sort = false, bool removesinglechars = false)
        {
            if (!string.IsNullOrEmpty(list))
            {
                string[] div = { divider };
                string[] items = list.Split(div, StringSplitOptions.RemoveEmptyEntries);
                items = items.Distinct().ToArray();
                if (sort)
                    Array.Sort(items);
                if (removesinglechars)
                {
                    string newlist = string.Empty;
                    foreach (string item in items)
                    {
                        if (item.Length > 1) newlist = AddItem(newlist, item, divider);
                    }
                    items = newlist.Split(div, StringSplitOptions.RemoveEmptyEntries);
                }
                list = String.Join(divider, items.Select(p => p.ToString()).ToArray());
            }
            return list;
        }
        private string RemoveShortWords(string input)
        {
            char[] delimiter = { ' ' };
            string[] array = input.Split(delimiter, StringSplitOptions.RemoveEmptyEntries);
            string output = "";
            foreach (string word in array)
            {
                if (word.Length > 2) output += word + " ";
            }
            if (output == "") return input;
            return output.Trim();
        }
        private List<PluginMultipleArtist> CreateMultipleArtists(string artist, List<SMMultiArtistCommands> multiartistcommands, List<string> excludemultiartists)
        {
            artist = artist.Replace(",", " , ");
            List<PluginMultipleArtist> multiartists = new List<PluginMultipleArtist>();
            int index = 0;
            bool endreached = false;
            do
            {
                PluginMultipleArtist multiartist = new PluginMultipleArtist();
                bool found = false;
                if (index != 0)
                {
                    if (artist.StartsWith(","))
                    {
                        multiartist.ArtistFunction = ",";
                        artist = artist.Substring(1).Trim();
                    }
                    else
                    {
                        foreach (SMMultiArtistCommands multiartistcommand in multiartistcommands)
                        {
                            if (artist.ToLower().StartsWith(multiartistcommand.Original.ToLower() + " "))
                            {
                                multiartist.ArtistFunction = multiartistcommand.Replacement;
                                artist = artist.Substring(multiartistcommand.Original.Length).Trim();
                                if (artist.StartsWith(". ")) artist = artist.Substring(2).Trim();
                                break;
                            }
                        }
                    }
                }
                else
                {
                    multiartist.ArtistFunction = "";
                }
                foreach (string excludemultiartist in excludemultiartists)
                {
                    if (artist.ToLower().StartsWith(excludemultiartist.Replace(", ", " , ").ToLower()) || artist.ToLower() == excludemultiartist.Replace(", ", " , ").ToLower())
                    {
                        found = true;
                        multiartist.Artist = excludemultiartist.Trim();
                        multiartist.ArtistSortOrder = CreateSortString(excludemultiartist.Trim());
                        if (artist.Length == excludemultiartist.Replace(", ", " , ").Length)
                        {
                            endreached = true;
                        }
                        else
                        {
                            artist = artist.Substring(excludemultiartist.Replace(", ", " , ").Length).Trim();
                        }
                        break;
                    }
                }
                if (!found)
                {
                    bool complete = false;
                    do
                    {
                        int divpos = artist.IndexOf(' ');
                        if (divpos == -1)
                        {
                            multiartist.Artist += " " + artist;
                            complete = true;
                            endreached = true;
                        }
                        else
                        {
                            multiartist.Artist += " " + artist.Substring(0, divpos);
                            artist = artist.Substring(divpos).Trim();
                        }
                        if (artist == "" || artist.StartsWith(","))
                        {
                            complete = true;
                        }
                        if (!complete)
                        {
                            foreach (SMMultiArtistCommands multiartistcommand in multiartistcommands)
                            {
                                if (artist.ToLower().StartsWith(multiartistcommand.Original.ToLower() + " "))
                                {
                                    complete = true;
                                    break;
                                }
                            }
                        }
                    } while (!complete);
                    multiartist.Artist = multiartist.Artist.Trim();
                }
                multiartist.Artist = multiartist.Artist.Trim();
                multiartist.ArtistSortOrder = CreateSortString(multiartist.Artist);
                multiartist.OrderNumber = index++;
                multiartists.Add(multiartist);
            } while (!endreached);
            return multiartists;
        }
        private string CreateArtistFromMultipleArtists(List<PluginMultipleArtist> multiartists, bool sortorder = false)
        {
            string artist = string.Empty;
            foreach (PluginMultipleArtist multiartist in multiartists)
            {
                if (!string.IsNullOrEmpty(multiartist.ArtistFunction))
                {
                    if (multiartist.ArtistFunction != ",")
                    {
                        artist += " ";
                    }
                    artist += multiartist.ArtistFunction + " ";
                }
                if (!sortorder)
                {
                    artist += multiartist.Artist;
                }
                else
                {
                    artist += multiartist.ArtistSortOrder;
                }
            }
            return artist;
        }
        private string CreateSortString(string input)
        {
            string[] articles = { "The", "A", "DJ", "MC", "El", "La", "Los", "Las", "Le", "Les", "Der", "Die", "Das", "Ein", "Eine", "Un", "Une" };
            foreach (string article in articles)
            {
                if (input.StartsWith(article + " "))
                {
                    return string.Format("{0}, {1}", input.Substring(article.Length + 1), article);
                }
            }
            return input;
        }
        private List<string> LoadExcludeArtists()
        {
            List<string> excludemultiartists = new List<string>();
            string file = Path.Combine(PluginConstants.SettingsPath, "extractartistsexclude.json");
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file);
                var items = JsonConvert.DeserializeObject<List<string>>(json);
                foreach (string item in items)
                {
                    excludemultiartists.Add(item);
                }
            }
            return excludemultiartists;
        }
        private bool SaveExcludeArtists(List<string> artists)
        {
            var settingsFile = Path.Combine(PluginConstants.SettingsPath, "extractartistsexclude.json");
            var json = JsonConvert.SerializeObject(artists);
            File.WriteAllText(settingsFile, json);
            return true;
        }
        private List<SMMultiArtistCommands> LoadMultiArtistCommands()
        {
            List<SMMultiArtistCommands> multiartistcommands = new List<SMMultiArtistCommands>();
            string file = Path.Combine(PluginConstants.SettingsPath, "extractartistscommands.json");
            if (File.Exists(file))
            {
                var json = File.ReadAllText(file);
                var items = JsonConvert.DeserializeObject<List<SMMultiArtistCommands>>(json);
                foreach (SMMultiArtistCommands item in items)
                {
                    multiartistcommands.Add(item);
                }
                SMMultiArtistCommands newitem = new SMMultiArtistCommands();
                newitem.Original = "/";
                newitem.Replacement = "/";
                multiartistcommands.Add(newitem);
            }
            return multiartistcommands;
        }
        public static string MyUrlEncode(string url, Encoding encoding)
        {
            if (url == null)
            {
                return null;
            }
            byte[] bytes = encoding.GetBytes(url);
            int num = 0;
            int num1 = 0;
            int length = (int)bytes.Length;
            for (int i = 0; i < length; i++)
            {
                char chr = (char)bytes[i];
                if (chr == ' ')
                {
                    num++;
                }
                else if (!IsSafe(chr))
                {
                    num1++;
                }
            }
            if ((num != 0 ? true : num1 != 0))
            {
                byte[] hex = new byte[length + num1 * 2];
                int num2 = 0;
                for (int j = 0; j < length; j++)
                {
                    byte num3 = bytes[j];
                    char chr1 = (char)num3;
                    if (IsSafe(chr1))
                    {
                        int num4 = num2;
                        num2 = num4 + 1;
                        hex[num4] = num3;
                    }
                    else if (chr1 != ' ')
                    {
                        int num5 = num2;
                        num2 = num5 + 1;
                        hex[num5] = 37;
                        int num6 = num2;
                        num2 = num6 + 1;
                        hex[num6] = (byte)IntToHex(num3 >> 4 & 15);
                        int num7 = num2;
                        num2 = num7 + 1;
                        hex[num7] = (byte)IntToHex(num3 & 15);
                    }
                    else
                    {
                        int num8 = num2;
                        num2 = num8 + 1;
                        hex[num8] = 43;
                    }
                }
                bytes = hex;
            }
            return encoding.GetString(bytes, 0, (int)bytes.Length);
        }
        private static bool IsSafe(char ch)
        {
            if (ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z' || ch >= '0' && ch <= '9')
            {
                return true;
            }
            char chr = ch;
            if (chr != '!')
            {
                switch (chr)
                {
                    case '\'':
                    case '(':
                    case ')':
                    case '*':
                    case '-':
                    case '.':
                        {
                            break;
                        }
                    case '+':
                    case ',':
                        {
                            return false;
                        }
                    default:
                        {
                            if (chr != '\u005F')
                            {
                                return false;
                            }
                            else
                            {
                                break;
                            }
                        }
                }
            }
            return true;
        }
        internal static char IntToHex(int n)
        {
            if (n <= 9)
            {
                return (char)(n + 48);
            }
            return (char)(n - 10 + 97);
        }

// settings related functions
        private SMChartsSettings GetSettings()
        {
            SMChartsSettings s = new SMChartsSettings();
            s.CountryIndex = 1;
            s.RecYear = true;
            s.Rating = true;
            s.AlbumChartsPeak = 2;
            s.AlbumChartsPeakWeeks = 0;
            s.AlbumChartsPoints = 0;
            s.AlbumChartsWeeks = 0;
            s.TrackChartsPeak = 1;
            s.TrackChartsPeakWeeks = 0;
            s.TrackChartsPoints = 7;
            s.TrackChartsWeeks = 5;
            return s;
        }

// settings class
        private class SMChartsAlbum
        {
            public string Artist { get; set; }
            public string Title { get; set; }
            public string Url { get; set; }
            public List<PluginTrackSearchResult> Releases { get; set; }
        }
        private class SMChartsSettings
        {
            public int CountryIndex { get; set; }
            public bool RecYear { get; set; }
            public bool Rating { get; set; }
            public int AlbumChartsPeak { get; set; }
            public int AlbumChartsPeakWeeks { get; set; }
            public int AlbumChartsWeeks { get; set; }
            public int AlbumChartsPoints { get; set; }
            public int TrackChartsPeak { get; set; }
            public int TrackChartsPeakWeeks { get; set; }
            public int TrackChartsWeeks { get; set; }
            public int TrackChartsPoints { get; set; }
        }
        private class SMMultiArtistCommands
        {
            public string Original { get; set; }
            public string Replacement { get; set; }
        }
        private class SMRelatedArtist
        {
            public string uniquename { get; set; }
            public string commonname { get; set; }
        }
        private class SMTrackTitle
        {
            public string Title { get; set; }
            public string Subtitle { get; set; }
            public string Remix { get; set; }
        }
        private class TrackInfo
        {
            public int TrackNumber { get; set; }
            public string Artist { get; set; }
            public string CatalogNumber { get; set; }
            public int Duration { get; set; }
            public int ReleaseType { get; set; }
            public string Remix { get; set; }
            public string Subtitle { get; set; }
            public string Title { get; set; }

            public override string ToString()
            {
                return JsonConvert.SerializeObject(this);
            }
            public static TrackInfo FromJson(string jsonText)
            {
                return JsonConvert.DeserializeObject<TrackInfo>(jsonText);
            }
        }
    }
}