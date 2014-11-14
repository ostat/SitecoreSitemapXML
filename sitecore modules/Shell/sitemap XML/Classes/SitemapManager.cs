/* *********************************************************************** *
 * File   : SitemapManager.cs                             Part of Sitecore *
 * Version: 1.0.0                                         www.sitecore.net *
 *                                                                         *
 *                                                                         *
 * Purpose: Manager class what contains all main logic                     *
 *                                                                         *
 * Copyright (C) 1999-2009 by Sitecore A/S. All rights reserved.           *
 *                                                                         *
 * This work is the property of:                                           *
 *                                                                         *
 *        Sitecore A/S                                                     *
 *        Meldahlsgade 5, 4.                                               *
 *        1613 Copenhagen V.                                               *
 *        Denmark                                                          *
 *                                                                         *
 * This is a Sitecore published work under Sitecore's                      *
 * shared source license.                                                  *
 *                                                                         *
 * *********************************************************************** */

using System.Collections.Generic;
using System.IO;
using System.Xml;
using Sitecore.Data.Items;
using Sitecore.Sites;
using Sitecore.Data;
using Sitecore.Configuration;
using Sitecore.Diagnostics;
using System.Web;
using System.Text;
using System.Linq;
using System.Collections.Specialized;
using System.Collections;

namespace Sitecore.Modules.SitemapXML
{
    public class SitemapManager
    {
        //private static string sitemapUrl;
        private const string protocol_Http = "http";
        private const string protocol_Https = "https";
        private const string protocol_sep = "://";


        private static StringDictionary m_Sites;
        public Database Db
        {
            get
            {
                Database database = Factory.GetDatabase(SitemapManagerConfiguration.WorkingDatabase);
                return database;
            }
        }

        public SitemapManager()
        {
            m_Sites = SitemapManagerConfiguration.GetSites();
            foreach (DictionaryEntry site in m_Sites)
            {
                BuildSiteMap(site.Key.ToString(), site.Value.ToString());
            }
        }


        private void BuildSiteMap(string sitename, string sitemapUrlNew)
        {
            try
            {
                Site site = Sitecore.Sites.SiteManager.GetSite(sitename);
                SiteContext siteContext = Factory.GetSite(sitename);
                string rootPath = siteContext.StartPath;

                List<Item> items = GetSitemapItems(rootPath);


                string fullPath = MainUtil.MapPath(string.Concat("/", sitemapUrlNew));
                string xmlContent = this.BuildSitemapXML(items, site);

                StreamWriter strWriter = new StreamWriter(fullPath, false);
                strWriter.Write(xmlContent);
                strWriter.Close();
            }
            catch (System.Exception ex)
            {
                throw new System.Exception(string.Format("unable to BuildSiteMap for sitename='{0}' sitemapUrlNew='{1}'", sitename, sitemapUrlNew), ex);
            }
        }



        public bool SubmitSitemapToSearchenginesByHttp()
        {
            if (!SitemapManagerConfiguration.IsProductionEnvironment)
                return false;

            bool result = false;
            Item sitemapConfig = Db.Items[SitemapManagerConfiguration.SitemapConfigurationItemPath];

            if (sitemapConfig != null)
            {
                string engines = sitemapConfig.Fields["Search engines"].Value;
                foreach (string id in engines.Split('|'))
                {
                    Item engine = Db.Items[id];
                    if (engine != null)
                    {
                        string engineHttpRequestString = engine.Fields["HttpRequestString"].Value;
                        foreach (string sitemapUrl in m_Sites.Values)
                            this.SubmitEngine(engineHttpRequestString, sitemapUrl);
                    }
                }
                result = true;
            }

            return result;
        }

        public void RegisterSitemapToRobotsFile()
        {

            string robotsPath = MainUtil.MapPath(string.Concat("/", "robots.txt"));
            StringBuilder sitemapContent = new StringBuilder(string.Empty);
            if (File.Exists(robotsPath))
            {
                StreamReader sr = new StreamReader(robotsPath);
                sitemapContent.Append(sr.ReadToEnd());
                sr.Close();
            }

            StreamWriter sw = new StreamWriter(robotsPath, false);
            foreach (string sitemapUrl in m_Sites.Values)
            {
                string sitemapLine = string.Concat("Sitemap: ", sitemapUrl);
                if (!sitemapContent.ToString().Contains(sitemapLine))
                {
                    sitemapContent.AppendLine(sitemapLine);
                }
            }
            sw.Write(sitemapContent.ToString());
            sw.Close();
        }

        private string BuildSitemapXML(List<Item> items, Site site)
        {
            XmlDocument doc = new XmlDocument();

            XmlNode declarationNode = doc.CreateXmlDeclaration("1.0", "UTF-8", null);
            doc.AppendChild(declarationNode);
            XmlNode urlsetNode = doc.CreateElement("urlset");
            XmlAttribute xmlnsAttr = doc.CreateAttribute("xmlns");
            xmlnsAttr.Value = SitemapManagerConfiguration.XmlnsTpl;
            urlsetNode.Attributes.Append(xmlnsAttr);

            doc.AppendChild(urlsetNode);


            foreach (Item itm in items)
            {
                doc = this.BuildSitemapItem(doc, itm, site);
            }

            return doc.OuterXml;
        }

        private XmlDocument BuildSitemapItem(XmlDocument doc, Item item, Site site)
        {
            string url = HtmlEncode(this.GetItemUrl(item, site));
            string lastMod = HtmlEncode(item.Statistics.Updated.ToString("yyyy-MM-ddTHH:mm:sszzz"));

            XmlNode urlsetNode = doc.LastChild;

            XmlNode urlNode = doc.CreateElement("url");
            urlsetNode.AppendChild(urlNode);

            XmlNode locNode = doc.CreateElement("loc");
            urlNode.AppendChild(locNode);
            locNode.AppendChild(doc.CreateTextNode(url));

            XmlNode lastmodNode = doc.CreateElement("lastmod");
            urlNode.AppendChild(lastmodNode);
            lastmodNode.AppendChild(doc.CreateTextNode(lastMod));

            return doc;
        }

        private string GetItemUrl(Item item, Site site)
        {
            Sitecore.Links.UrlOptions options = Sitecore.Links.UrlOptions.DefaultOptions;

            options.SiteResolving = Sitecore.Configuration.Settings.Rendering.SiteResolving;
            options.Site = SiteContext.GetSite(site.Name);
            options.AlwaysIncludeServerUrl = false;
           
            string url = Sitecore.Links.LinkManager.GetItemUrl(item, options);
      
            string serverUrl = SitemapManagerConfiguration.GetServerUrlBySite(site.Name);
            string protocol = SitemapManagerConfiguration.GetProtocolBySite(site.Name);

            var scheme = protocol_Http + protocol_sep;
            if (serverUrl.Contains(scheme))
            {
                serverUrl = serverUrl.Substring(scheme.Length);
            }
            scheme = protocol_Https + protocol_sep;
            if (serverUrl.Contains(scheme))
            {
                serverUrl = serverUrl.Substring(scheme.Length);
            }
            scheme = protocol + protocol_sep;

            StringBuilder sb = new StringBuilder();

            if (!string.IsNullOrEmpty(serverUrl))
            {
                if (url.Contains(protocol_sep) && !url.Contains(protocol))
                {
                    sb.Append(protocol + protocol_sep);
                    sb.Append(serverUrl);
                    if (url.IndexOf("/", 3) > 0)
                        sb.Append(url.Substring(url.IndexOf("/", 3)));
                }
                else
                {
                    sb.Append(scheme);
                    sb.Append(serverUrl);
                    sb.Append(url);
                }
            }
            else if (!string.IsNullOrEmpty(site.Properties["hostname"]))
            {
                sb.Append(scheme);
                sb.Append(site.Properties["hostname"]);
                sb.Append(url);
            }
            else
            {
                if (url.Contains(protocol_sep) && !url.Contains(protocol))
                {
                    sb.Append(scheme);
                    sb.Append(url);
                }
                else
                {
                    sb.Append(Sitecore.Web.WebUtil.GetFullUrl(url));
                }
            }

            var result = sb.ToString();
            Log.Debug(string.Format("Getting url for item: SiteName:{1} ServerUrl:{2} protocol:{4} URL:{0} \n item url result:{5}", url, site.Name, serverUrl, protocol, result), this);

            return result;

        }

        private static string HtmlEncode(string text)
        {
            string result = HttpUtility.HtmlEncode(text);

            return result;
        }

        private void SubmitEngine(string engine, string sitemapUrl)
        {
            //Check if it is not localhost because search engines returns an error
            if (!sitemapUrl.Contains("http://localhost"))
            {
                string request = string.Concat(engine, HtmlEncode(sitemapUrl));

                System.Net.HttpWebRequest httpRequest = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(request);
                try
                {
                    System.Net.WebResponse webResponse = httpRequest.GetResponse();

                    System.Net.HttpWebResponse httpResponse = (System.Net.HttpWebResponse)webResponse;
                    if (httpResponse.StatusCode != System.Net.HttpStatusCode.OK)
                    {
                        Log.Error(string.Format("Cannot submit sitemap to \"{0}\"", engine), this);
                    }
                }
                catch
                {
                    Log.Warn(string.Format("The serachengine \"{0}\" returns an 404 error", request), this);
                }
            }
        }


        private List<Item> GetSitemapItems(string rootPath)
        {
            string disTpls = SitemapManagerConfiguration.EnabledTemplates;
            string exclNames = SitemapManagerConfiguration.ExcludeItems;


            Database database = Factory.GetDatabase(SitemapManagerConfiguration.WorkingDatabase);
            if (database == null)
            {
                throw new Exceptions.InvalidItemException("Database item is null");
            }
            Item contentRoot = database.Items[rootPath];
            if (contentRoot == null)
            {
                throw new Exceptions.InvalidItemException(string.Format("Unable to get access to root item: '{0}'", rootPath));
            }
            Item[] descendants;
            Sitecore.Security.Accounts.User user = Sitecore.Security.Accounts.User.FromName(@"extranet\Anonymous", true);
            using (new Sitecore.Security.Accounts.UserSwitcher(user))
            {
                descendants = contentRoot.Axes.GetDescendants();
            }
            List<Item> sitemapItems = descendants.ToList();
            sitemapItems.Insert(0, contentRoot);

            List<string> enabledTemplates = this.BuildListFromString(disTpls, '|');
            List<string> excludedNames = this.BuildListFromString(exclNames, '|');


            var selected = from itm in sitemapItems
                           where itm.Template != null && enabledTemplates.Contains(itm.Template.ID.ToString()) &&
                                    !excludedNames.Contains(itm.ID.ToString())
                           select itm;

            return selected.ToList();
        }

        private List<string> BuildListFromString(string str, char separator)
        {
            string[] enabledTemplates = str.Split(separator);
            var selected = from dtp in enabledTemplates
                           where !string.IsNullOrEmpty(dtp)
                           select dtp;

            List<string> result = selected.ToList();

            return result;
        }

    }
}
