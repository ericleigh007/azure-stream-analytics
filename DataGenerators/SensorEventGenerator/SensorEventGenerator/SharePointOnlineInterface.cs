using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;

using Microsoft.Azure.WebJobs.Host;

using System.Globalization;
using System.Runtime.InteropServices;
using System.Net;
using Microsoft.Graph;
using Newtonsoft.Json.Linq;
using Microsoft.SharePoint.Client;
using SP = Microsoft.SharePoint.Client;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Security;
using System.Data;
using System.Dynamic;
using System.Net.Http;
using System.Net.Http.Headers;
using MiscellaneousJunk;

namespace SharepointOnlineInterface
{
    public static class SharePointOnlineInterface
    {
        #region Config Values
        // The Client ID is used by the application to uniquely identify itself to Azure AD.
        // The Tenant is the name of the Azure AD tenant in which this application is registered.
        // The AAD Instance is the instance of Azure, for example public Azure or Azure China.
        // The Redirect URI is the URI where Azure AD will return OAuth responses.
        // The Authority is the sign-in URL of the tenant.
        //
#if false
    private static string aadInstance = ConfigurationManager.AppSettings["idaAADInstance"];
    private static string tenant = ConfigurationManager.AppSettings["idaTenant"];
    private static string resourceId = ConfigurationManager.AppSettings["idaResource"];
    private static string clientId = ConfigurationManager.AppSettings["idaClientId"];
    private static string appKey = ConfigurationManager.AppSettings["idaAppKey"];
    private static string authority = String.Format(CultureInfo.InvariantCulture, aadInstance, tenant);
    private static string sharepointSite = ConfigurationManager.AppSettings["SharepointSiteName"];

    private static string spo_user = ConfigurationManager.AppSettings["SPOuser"];
    private static string spo_pwd = ConfigurationManager.AppSettings["SPOpwd"];

    public static int DELETE_CHUNK_SIZE = int.Parse(ConfigurationManager.AppSettings["DELETE_CHUNK_SIZE"]);
    public static int ADD_CHUNK_SIZE = int.Parse(ConfigurationManager.AppSettings["ADD_CHUNK_SIZE"]);

    /*
    private static string graphResourceId = ConfigurationManager.AppSettings["ida:GraphResourceId"];
    private static string graphApiVersion = ConfigurationManager.AppSettings["ida:GraphApiVersion"];
    private static string graphApiEndpoint = ConfigurationManager.AppSettings["ida:GraphEndpoint"];
    */
#else
        private static string aadInstance = ""; //  Environment.GetEnvironmentVariable("idaAADInstance");
        private static string tenant = ""; // Environment.GetEnvironmentVariable("idaTenant");
        private static string resourceId = ""; //  Environment.GetEnvironmentVariable("idaResource");
        private static string clientId = ""; //  Environment.GetEnvironmentVariable("idaClientId");
        private static string appKey = ""; //  Environment.GetEnvironmentVariable("idaAppKey");
        private static string authority = ""; //  String.Format(CultureInfo.InvariantCulture, aadInstance, tenant);
        private static string sharepointSite = ""; //  Environment.GetEnvironmentVariable("SharepointSiteName");


        private static string spo_user = ""; //  Environment.GetEnvironmentVariable("SPOuser");
        private static string spo_pwd = "";  //  Environment.GetEnvironmentVariable("SPOpwd");

        public static int DELETE_CHUNK_SIZE = 200; //  int.Parse(Environment.GetEnvironmentVariable("DELETE_CHUNK_SIZE"));
        public static int ADD_CHUNK_SIZE = 200; //  int.Parse(Environment.GetEnvironmentVariable("ADD_CHUNK_SIZE"));

        /*
        private static string graphResourceId = Environment.GetEnvironmentVariable("ida:GraphResourceId");
        private static string graphApiVersion = Environment.GetEnvironmentVariable("ida:GraphApiVersion");
        private static string graphApiEndpoint = Environment.GetEnvironmentVariable("ida:GraphEndpoint");
        */
#endif

        #endregion

        #region Thread-Specific

        [ThreadStatic]
        private static SharePointOnlineCredentials SPOcredentials = null;

        [ThreadStatic]
        public static bool signedIn = false;

        [ThreadStatic]
        private static ClientContext SPOCurrentContext = null;

        [ThreadStatic]
        private static SP.List SPOCurrentList = null;

        [ThreadStatic]
        private static TraceWriter _log = null;

        #endregion

        private static HttpClient httpClient = new HttpClient();
        private static AuthenticationContext authContext = null;
        private static ClientCredential graphCredential = null;
        private static AuthenticationResult authResult = null;

        public static string lastError = "";
        public static string userName = "";

        public static Dictionary<string, string> ObjToSPDataTypeConverter = new Dictionary<string, string>()
        {
            { "System.String" , "Text"},
            { "System.Int" , "Integer" },
            { "System.Int32" , "Integer" },
            { "System.DateTime", "DateTime" },
            { "System.Decimal", "Number" },
            { "System.DBNull", "ElsNull" }  // catch the nulls to avoid a lot of console noise
        };

        public static Dictionary<string, string> SQLtoSPdataTypeConverter = new Dictionary<string, string>()
        {
            { "nvarchar" , "Text"},
            { "int" , "Integer" },
            { "int32" , "Integer" },
            { "datetime2", "DateTime" },
            { "datetime", "DateTime" },
            { "decimal", "Number" }
        };

        public static List<JToken> resultsList = new List<JToken>();

        // https://azure.microsoft.com/en-us/documentation/articles/active-directory-devquickstarts-dotnet/
        // app: https://manage.windowsazure.com/raydon.com#Workspaces/ActiveDirectoryExtension/Directory/76395792-50d9-499d-9134-2a51a321f590/ClientApp/2507d400-bfdf-4963-8607-608511a7cccf/clientAppConfigure
        // Sharepoint REST: https://msdn.microsoft.com/en-us/library/office/dn292552.aspx

        static SharePointOnlineInterface()
        {
            if (!String.IsNullOrWhiteSpace(authority))
            {
                authContext = new AuthenticationContext(authority); //  new FileCache());
            }

            if (!String.IsNullOrWhiteSpace(clientId) && !String.IsNullOrWhiteSpace(appKey))
            {
                graphCredential = new ClientCredential(clientId, appKey);
            }
        }

        public static bool SetCredentials( string sp_user, string sp_pwd)
        {
            spo_user = sp_user;
            spo_pwd = sp_pwd;

            return true;
        }

        public static void SetLog( TraceWriter log)
        {
            _log = log;
        }

        public static ClientContext SetClientContext(string siteName, bool force_new = false)
        {
            if (!signedIn)
            {
                var signed = SignIn().Result;
            }

            if (force_new || SPOCurrentContext == null || SPOCurrentContext.Url != siteName)
            {
                if (SPOCurrentContext != null) SPOCurrentContext.Dispose();
                SPOCurrentContext = new ClientContext(siteName);
                SPOCurrentContext.Credentials = SPOcredentials;
            }

            return SPOCurrentContext;
        }

        public static SP.List SetCurrentList(string listName, bool force_new = false)
        {
            Misc.ReportWarning($"  SetCurrentList is not implemented completely");

            if ( force_new || SPOCurrentList == null )
            {
                var ctx = SPOCurrentContext;
                SPOCurrentList = ctx.Web.Lists.GetByTitle(listName);
            }

            return SPOCurrentList;
        }

        public static async Task<bool> CheckForCachedToken()
        {
            // As the application starts, try to get an access token without prompting the user.  If one exists, show the user as signed in.
            AuthenticationResult result = null;
            try
            {
                result = await authContext.AcquireTokenAsync(resourceId, graphCredential);
            }
            catch (AdalException ex)
            {
                if (ex.ErrorCode != "user_interaction_required")
                {
                    // An unexpected error occurred.
                    lastError = ex.Message;
                    Console.WriteLine($"exception signing in : {ex.Message}");

                }

                // If user interaction is required, proceed to main page without singing the user in.
                return true;
            }

            // A valid token is in the cache
            userName = result.UserInfo.DisplayableId;
            return true;
        }

        public static async Task<bool> SignIn()
        {
            // Get an Access Token for the different APIs
            try
            {
                // graph not used for now
//                authResult = await authContext.AcquireTokenAsync(resourceId, graphCredential);
                // look at security later

                // Sharepoint online credentials
                SecureString ss = new SecureString();
                foreach (char s in spo_pwd)
                {
                    ss.AppendChar(s);
                }

                SPOcredentials = new SharePointOnlineCredentials(spo_user, ss);

                // only valid for graph
                //                userName = authResult.UserInfo.DisplayableId;
                signedIn = true;
            }
            catch (AdalException ex)
            {
                // An unexpected error occurred, or user canceled the sign in.
                if (ex.ErrorCode != "access_denied")
                {
                    lastError = ex.Message;
                }

                return false;
            }

            return true;
        }

        public static void SignOut()
        {
            /*
            // Clear the token cache
            authContext.TokenCache.Clear();

            // Clear cookies from the browser control.
            ClearCookies();

            // Reset the UI
            signedIn = false;
            userName = "";
            */
        }

        #region REST methods

        /*
        public static async Task<bool> SearchUsersFor(string searchText)
        {
            // Validate the Input String
            if (string.IsNullOrEmpty(searchText))
            {
                lastError = "Please enter a string";
                return false;
            }

            if (!SignIn().Result)
            {
                lastError = "cannot sign in";
                return false;
            }

            // Once we have an access_token, search for users.
            try
            {
                string graphRequest = String.Format(CultureInfo.InvariantCulture, "{0}{1}/users?api-version={2}&$filter=startswith(userPrincipalName, '{3}')", graphApiEndpoint, tenant, graphApiVersion, searchText);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, graphRequest);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
                HttpResponseMessage response = httpClient.SendAsync(request).Result;

                if (!response.IsSuccessStatusCode)
                    throw new WebException(response.StatusCode.ToString() + ": " + response.ReasonPhrase);

                string content = response.Content.ReadAsStringAsync().Result;
                JObject jResult = JObject.Parse(content);

                if (jResult["odata.error"] != null)
                    throw new Exception((string)jResult["odata.error"]["message"]["value"]);

                // Add search results to list;
                resultsList = jResult["value"].ToList();
            }
            catch (Exception ex)
            {
                lastError = "Error: " + ex.Message;
                return false;
            }

            return true;
        }
        */

        public static async Task<string> getWebTitle(string webUrl)
        {
            //Creating Password 
            const string RESTURL = "{0}/_api/web?$select=Title";

            //Creating Credentials 
            var passWord = new SecureString();
            foreach (var c in spo_pwd) passWord.AppendChar(c);
            var credential = new SharePointOnlineCredentials(spo_user, passWord);

            //Creating Handler to allows the client to use credentials and cookie 
            using (var handler = new HttpClientHandler() { Credentials = credential })
            {
                //Getting authentication cookies 
                Uri uri = new Uri(webUrl);
                handler.CookieContainer.SetCookies(uri, credential.GetAuthenticationCookie(uri));

                //Invoking REST API 
                using (var client = new HttpClient(handler))
                {
                    client.DefaultRequestHeaders.Accept.Clear();
                    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    HttpResponseMessage response = await client.GetAsync(string.Format(RESTURL, webUrl)).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();

                    string jsonData = await response.Content.ReadAsStringAsync();

                    return jsonData;
                }
            }
        }

        public static JToken GetListItems(Uri webUri, string listTitle)
        {
            var ans = SignIn().Result;
            using (var client = new WebClient())
            {
                client.Headers.Add("X-FORMS_BASED_AUTH_ACCEPTED", "f");
                client.Credentials = SPOcredentials;
                client.Headers.Add(HttpRequestHeader.ContentType, "application/json;odata=verbose");
                client.Headers.Add(HttpRequestHeader.Accept, "application/json;odata=verbose");
                var endpointUri = new Uri(webUri, string.Format("/sites/RaydonCommonData/_api/web/lists/getbytitle('{0}')/Items", listTitle));
                var result = client.DownloadString(endpointUri);
                var t = JToken.Parse(result);
                //                var listID = t["d"]["Id"];
                //                endpointUri = new Uri(webUri, string.Format("/sites/RaydonCommonData/_api/web/lists/{0}/Items)", listID));
                //                result = client.DownloadString(endpointUri);
                return t["d"]["results"];
            }
        }

        public static JToken GetListFields(Uri webUri, string listTitle)
        {
            var ans = SignIn().Result;
            using (var client = new WebClient())
            {
                client.Headers.Add("X-FORMS_BASED_AUTH_ACCEPTED", "f");
                client.Credentials = SPOcredentials;
                client.Headers.Add(HttpRequestHeader.ContentType, "application/json;odata=verbose");
                client.Headers.Add(HttpRequestHeader.Accept, "application/json;odata=verbose");
                var endpointUri = new Uri(webUri, string.Format("/sites/RaydonCommonData/_api/web/lists/getbytitle('{0}')/Fields", listTitle));
                string result = null;
                try
                {
                    result = client.DownloadString(endpointUri);
                    var t = JToken.Parse(result);
                    //                var listID = t["d"]["Id"];
                    //                endpointUri = new Uri(webUri, string.Format("/sites/RaydonCommonData/_api/web/lists/{0}/Items)", listID));
                    //                result = client.DownloadString(endpointUri);
                    return t["d"]["results"];
                }
                catch (WebException wex)
                {
                    Console.WriteLine($"error obtaining fields for list {webUri} {listTitle}.   Does the list exist?");
                    return null;
                }
            }
        }

        /*
                string graphRequest = String.Format(CultureInfo.InvariantCulture, sharepointOnLineList, sharepointSite, siteName, listName);
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, graphRequest);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authResult.AccessToken);
                HttpResponseMessage response = await httpClient.SendAsync(request);

                        if (!response.IsSuccessStatusCode)
                            throw new WebException(response.StatusCode.ToString() + ": " + response.ReasonPhrase);

                        string content = await response.Content.ReadAsStringAsync();
                JObject jResult = JObject.Parse(content);

                        if (jResult["odata.error"] != null)
                            throw new Exception((string)jResult["odata.error"]["message"]["value"]);

                        // Add search results to list;
                        resultsList = jResult["value"].ToList();
        */

        #endregion

        #region CSOM methods
        public static void ExecuteQuery()
        {
            if (SPOCurrentContext != null)
            {
                SPOCurrentContext.ExecuteQuery();
            }
        }

        public static ListCollection GetAllLists(string siteName)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            // Starting with ClientContext, the constructor requires a URL to the 
            // server running SharePoint. 
            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            // The SharePoint web at the URL.
            Web web = context.Web;

            // Retrieve all lists from the server. 
            context.Load(web.Lists,
                         lists => lists.Include(list => list.Title, // For each list, retrieve Title and Id. 
                                                list => list.Id));

            // Execute query. 
            context.ExecuteQuery();

            return web.Lists;
        }

        public static SP.List CreateList(string siteName, string listName, string listTitle, string listDescription = "")
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            // Starting with ClientContext, the constructor requires a URL to the 
            // server running SharePoint. 
            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            // The SharePoint web at the URL.
            Web web = context.Web;

            ListCreationInformation creationInfo = new ListCreationInformation();
            creationInfo.Title = listTitle;
            creationInfo.TemplateType = (int)ListTemplateType.GenericList;
            SP.List list = web.Lists.Add(creationInfo);

            context.Load(list);

            if (listDescription.Length > 0)
            {
                list.Description = listDescription + $", copied by {Assembly.GetExecutingAssembly().GetName().Name}, "
                                                   + $"Version {Assembly.GetExecutingAssembly().GetName().Version.ToString()}, "
                                                   + $"on {DateTimeOffset.UtcNow} UTC";
            }
            else
            {
                list.Description = listDescription + $"created by {Assembly.GetExecutingAssembly().GetName().Name}, "
                                                   + $"Version {Assembly.GetExecutingAssembly().GetName().Version.ToString()}, "
                                                   + $"on {DateTimeOffset.UtcNow} UTC";
            }

            list.Update();

            context.ExecuteQuery();

            return list;
        }

        public static FieldCollection GetAllFields(string siteName, string listName)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List list = context.Web.Lists.GetByTitle(listName);
            SP.FieldCollection listFields = list.Fields;

            context.Load(list);
            context.Load(listFields);

            // We must call ExecuteQuery before enumerate list.Fields. 
            context.ExecuteQuery();

            return listFields;
        }

        // pass keepFieldNames, which is a list of INTERNAL names, if you want to 
        // keep some fields in the output list.
        public static bool DeleteAllFields( string siteName, string listName, List<string> keepFieldNames = null)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List list = context.Web.Lists.GetByTitle(listName);
            SP.FieldCollection listFields = list.Fields;

            context.Load(list);

            context.ExecuteQuery();

            context.Load(listFields);

            context.ExecuteQuery();

            try
            {
                // if this throws an exception, this is a new list with no fields
                // if we don't have any fields, no need to delete any
                if (listFields.Count == 0)
                {
                    return true;
                }
            }
            catch( Exception ex )
            {
                return true;
            }

            List<Field> to_delete = new List<Field>();

            foreach( var field in listFields)
            {
                bool delete_it = !field.Hidden;
                delete_it &= field.Group != "_Hidden";
                delete_it &= !field.FromBaseType;
                delete_it &= field.CanBeDeleted == true;

                bool skipField = false;
                if (keepFieldNames != null)
                {
                    skipField = keepFieldNames.Where(p => p == field.InternalName).FirstOrDefault() != null;
                }

                delete_it &= !skipField;

                if (delete_it)
                {
                    to_delete.Add(field);
                }
            }

            foreach( var df in to_delete)
            {
                df.DeleteObject();
            }

            context.ExecuteQuery();

            return true;
        }

        public static bool CreateAllFields( string siteName, string listName, FieldCollection fields)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List list = context.Web.Lists.GetByTitle(listName);
            SP.FieldCollection listFields = list.Fields;

            context.Load(list);
            context.Load(listFields);

            context.ExecuteQuery();

            var createdFields = new List<string>();
            var skippedFields = new List<string>();

            var prunedListI = fields.Where(p => p.CanBeDeleted).Where(p => !p.FromBaseType).ToList();
            var prunedListO = listFields.Where(p => p.CanBeDeleted).Where(p => !p.FromBaseType).ToList();

            Misc.ReportInfo($"  input list has {prunedListI.Count} fields to be copied.");

            foreach ( var field in prunedListI)
            {
                // Skip the field if it already exists in the output list
                // todo: may need more checks here
                var existing_field = prunedListO.Where(p => p.InternalName == field.InternalName).FirstOrDefault();
                if ( (existing_field != null) && ( existing_field.TypeAsString == field.TypeAsString))
                { 
//                    Misc.ReportInfo($"  existing field '{field.InternalName}' skipped");
                    skippedFields.Add($"{field.Title}, ({field.InternalName})");
                }
                else
                {
                    // getting the SchemaXml from the source field and creating the new field with it
                    listFields.AddFieldAsXml(field.SchemaXml, true, 0);
                    createdFields.Add($"{field.Title}, ({field.InternalName})");
                }
            }

            context.ExecuteQuery();

            if ( createdFields.Count > 0)
            {
                Misc.ReportInfo($"created {createdFields.Count} new fields");
                var str = String.Join(",", createdFields);
                Misc.ReportInfo($"{str}");
            }

            if ( skippedFields.Count > 0)
            {
                Misc.ReportInfo($"skipped {skippedFields.Count} existing fields");
                var str = String.Join(",", skippedFields);
                Misc.ReportInfo($"{str}");
            }

            return true;
        }

        // this method is primarily used when a list is being made from other data.
        // The field types are expected to be SQL type names, which are converted to sharepoint
        // types as accurately as possible
        public static bool CreateAllFields(string siteName, string listName, List<Object[]> field_info_rows)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List list = context.Web.Lists.GetByTitle(listName);
            SP.FieldCollection listFields = list.Fields;

            context.Load(list);
            context.Load(listFields);

            context.ExecuteQuery();

            var names = field_info_rows[0];
            var types = field_info_rows[1];
            var typeNames = field_info_rows[2];
            var typeLengths = field_info_rows[3];

            var fieldCount = names.Length;

            var createdFields = new List<string>();
            var skippedFields = new List<string>();

            var prunedListO = listFields.Where(p => p.CanBeDeleted).Where(p => !p.FromBaseType).ToList();

            int c = 0;
            for( c = 0; c < fieldCount; c++ )
            {
                var fieldTitle = names[c] as String;
                var fieldSPType = SQLtoSPdataTypeConverter[typeNames[c] as String];

                var existing_field = prunedListO.Where(p => p.Title == fieldTitle).FirstOrDefault();
                if ((existing_field != null) && (existing_field.TypeAsString == fieldSPType))
                {
//                    Misc.ReportInfo($"  existing field '{existing_field.Title}' ({existing_field.InternalName}) skipped");
                    skippedFields.Add($"{existing_field.Title}, ({existing_field.InternalName})");
                }
                else
                {
                    SP.Field field = null;
                    if (fieldSPType == "Number")
                    {
                        // if we don't specify the "Decimals" attribute, the number is a whole number, which
                        // breaks a lot of stuff (note the use of '5' means we're not getting precision data,
                        // mostly financials and money, larger fractions and the like.
                        field = listFields.AddFieldAsXml(
                            $"<Field DisplayName='{fieldTitle}' Type='{fieldSPType}' Decimals='5'/>",
                            true,
                            AddFieldOptions.AddToNoContentType);
                    }
                    else
                    {
                        field = listFields.AddFieldAsXml(
                            $"<Field DisplayName='{fieldTitle}' Type='{fieldSPType}' />",
                            true,
                            AddFieldOptions.AddToNoContentType);
                    }

                    createdFields.Add($"{fieldTitle}");
                }
            }

            context.ExecuteQuery();

            if (createdFields.Count > 0)
            {
                Misc.ReportInfo($"created {createdFields.Count} new fields");
                var str = String.Join(",", createdFields);
                Misc.ReportInfo($"{str}");
            }

            if (skippedFields.Count > 0)
            {
                Misc.ReportInfo($"skipped {skippedFields.Count} existing fields");
                var str = String.Join(",", skippedFields);
                Misc.ReportInfo($"{str}");
            }

            return true;
        }

        // this is a sort of special case add field.  Limited to use cases driven by sharepoint data input 
        // (type is used directly)
        public static bool AddField(string siteName, string listName, string fieldDisplayName, string fieldSPType)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List list = context.Web.Lists.GetByTitle(listName);
            SP.FieldCollection listFields = list.Fields;

            context.Load(list);
            context.Load(listFields);

            if (fieldSPType == "Number")
            {
                SP.Field field = listFields.AddFieldAsXml(
                    $"<Field DisplayName='{fieldDisplayName}' Type='{fieldSPType}' Decimals='5'/>",
                    true,
                    AddFieldOptions.AddToNoContentType);
            }
            else
            {
                SP.Field field = listFields.AddFieldAsXml(
                    $"<Field DisplayName='{fieldDisplayName}' Type='{fieldSPType}' />",
                    true,
                    AddFieldOptions.AddToNoContentType);
            }

            context.ExecuteQuery();

            return true;
        }

        public static bool DeleteField(string siteName, string listName, string fielduniqueName)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List list = context.Web.Lists.GetByTitle(listName);
            SP.FieldCollection listFields = list.Fields;

            context.Load(list);
            context.Load(listFields);

            SP.Field field = listFields.GetByInternalNameOrTitle(fielduniqueName);

            field.DeleteObject();

            context.ExecuteQuery();

            return true;
        }


        // The caller MUST call this until the return is positive, indicating that all rows
        // were deleted.  For use in time-limited operations like Azure functions or lambda,
        // one should check to see if timing is nearly expired after deleting a number of rows
        // if time is expired, one would have to retry the operation (after having deleted)
        // some items already.
        //
        //   while ( returnRows < 0 )
        //   {
        //       returnRows = DeleteAllItems(max_items:1000...)
        //       // check here to see if we're out of time and exit if so
        //   }
        //
        public static int DeleteAllItems(string siteName, string listName, int max_items = 5000)
        {
            // Hack to protect against logic errors causing data loss
            if (listName == "DataMigrationList") return 0;

            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List theList = context.Web.Lists.GetByTitle(listName);
            ListItemCollection items = null;

            // This creates a CamlQuery that has a RowLimit of max_items, and also specifies Scope="RecursiveAll" 
            // so that it grabs all list items, regardless of the folder they are in. 
            CamlQuery query = CamlQuery.CreateAllItemsQuery(max_items);

            context.Load(theList);
            int rows_deleted = 0;

            items = theList.GetItems(query);

            // Retrieve all items in the ListItemCollection from List.GetItems(Query). 
            context.Load(items);
            context.ExecuteQuery();

            Misc.ReportInfo($"deleting queried items from {siteName}/{listName} -- {theList.ItemCount} items left");

            for (int i = items.Count - 1; i >= 0; i--)
            {
                items[i].DeleteObject();
                rows_deleted++;

                if ((i % DELETE_CHUNK_SIZE) == 0)
                {
                    context.ExecuteQuery();

                    Misc.ReportInfo($"deleting chunks from {siteName}/{listName} -- {items.Count} items left");
                }
            }

            context.Load(theList);

            context.ExecuteQuery();

            if ( theList.ItemCount > 0 )
            {
                return -rows_deleted; // indication to the caller that not all were deleted
            }

            return rows_deleted;
        }

        public static SP.List GetList(string siteName, string listName)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.ListCollection siteLists = context.Web.Lists;
            context.Load(siteLists);
            SP.List theList = siteLists.GetByTitle(listName);
            context.Load(theList);

            try
            {
                context.ExecuteQuery();
            }
            catch( Exception ex )
            {
                return null;
            }

            return theList;
        }

        public static SP.List GetList(string siteName, Guid listId)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.ListCollection siteLists = context.Web.Lists;
            context.Load(siteLists);
            SP.List theList = siteLists.GetById(listId);
            context.Load(theList);

            try
            {
                context.ExecuteQuery();
            }
            catch (Exception ex)
            {
                return null;
            }

            return theList;
        }

        public static List<ListItem> GetAllItems(string siteName, string listName, int max_items = 10000,
                                                     string sortByField = "", bool sortAscending = true)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List theList = context.Web.Lists.GetByTitle(listName);
            ListItemCollection items = null;

            // This creates a CamlQuery that has a RowLimit of max_items, and also specifies Scope="RecursiveAll" 
            // so that it grabs all list items, regardless of the folder they are in. 
            CamlQuery query = CamlQuery.CreateAllItemsQuery(max_items);
            items = theList.GetItems(query);

            // Retrieve all items in the ListItemCollection from List.GetItems(Query). 
            context.Load(items);
            context.ExecuteQuery();

            var outList = new List<ListItem>();

            if ( !String.IsNullOrWhiteSpace(sortByField))
            {
                if ( sortAscending )
                {
                    outList = items.OrderBy(p => p[sortByField]).ToList();
                }
                else
                {
                    outList = items.OrderByDescending(p => p[sortByField]).ToList();
                }
            }
            else
            {
                outList = items.ToList();
            }

            return outList;
        }

        public static ListItem GetItem(string siteName, string listName, Guid itemGUID)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List theList = context.Web.Lists.GetByTitle(listName);

            ListItem item = theList.GetItemByUniqueId(itemGUID);

            // Retrieve all items in the ListItemCollection from List.GetItems(Query). 
            context.Load(item);
            context.ExecuteQuery();

            return item;
        }

        public static ListItem GetItem(string siteName, Guid listGUID, string ItemID)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List theList = context.Web.Lists.GetById(listGUID);

            ListItem item = theList.GetItemById(ItemID);

            // Retrieve all items in the ListItemCollection from List.GetItems(Query). 
            context.Load(item);
            context.ExecuteQuery();

            return item;
        }

        public static bool AddItemToList(string siteName, string listName, SP.FieldCollection fields, SP.ListItem item, bool batch = false)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            // Caches, but in thread-local storage
            ClientContext context = SetClientContext(siteName);
            //            SP.List theList = SetCurrentList(listName, false);
            SP.List theList = context.Web.Lists.GetByTitle(listName);

            ListItemCreationInformation itemCreateInfo = new ListItemCreationInformation();
            ListItem newItem = theList.AddItem(itemCreateInfo);

            foreach( var field in fields)
            {
                bool copy_field = !field.Hidden;
                copy_field &= field.Group != "_Hidden";
                copy_field &= !field.ReadOnlyField;
                copy_field &= field.InternalName != "Attachments";
                copy_field &= field.InternalName != "ContentType";

                if ( copy_field )
                {
                    newItem[field.InternalName] = item[field.InternalName];
                }
            }

            newItem.Update();

            if (!batch)
            {
                context.ExecuteQuery();
            }

            return true;
        }

        public static bool AddItemToList(string siteName, string listName, Object[] field_values, Object[] field_names, Object[] field_types, bool batch = false)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = SetClientContext(siteName);
            //            SP.List theList = SetCurrentList(listName, false);
            SP.List theList = context.Web.Lists.GetByTitle(listName);

            // We are just creating a regular list item, so we don't need to 
            // set any properties. If we wanted to create a new folder, for 
            // example, we would have to set properties such as 
            // UnderlyingObjectType to FileSystemObjectType.Folder. 
            ListItemCreationInformation itemCreateInfo = new ListItemCreationInformation();
            ListItem newItem = theList.AddItem(itemCreateInfo);

            // foreach through the objects and build the item
            for (int i = 0; i < field_values.Length; i++)
            {
                var o = field_values[i];
                if ( o == null || o.GetType() == typeof(System.DBNull))
                {
                    // Leave this field in the item as default
                    continue;
                }

                // this is the column name
                string fieldName = field_names[i].ToString();
                // sp column type
                string SPType = field_types[i].ToString();

                try
                {
                    switch (SPType)
                    {
                        case "Text":
                            newItem[fieldName] = o.ToString();
                            break;

                        case "Integer":
                            newItem[fieldName] = (int)o;
                            break;

                        case "Number":
                            newItem[fieldName] = (decimal)o;
                            break;

                        case "DateTime":
                            newItem[fieldName] = (DateTime)o;
                            break;

                        case "ElsNull":
                            // don't do anything with nulls
                            break;

                        default:
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Misc.ReportWarning($"data for item '{fieldName}' of type '{SPType}' cannot be updated");
                }
            }

            // the "batch" switch means that the caller will take care of calling ExecuteQuery often enough to avoid throttling
            // errors
            newItem.Update();

            if (!batch)
            {
                context.ExecuteQuery();
            }

            return true;
        }

        // The method takes a dictionary of <string,object> where the keys are the "Display Names" 
        // or "Title" or "InternalName" of the field to be updateed, and the values are the object values
        // to be placed in them.  If a key within the dictionary matches no field Title or Internal
        // name, that field will be skipped.
        public static bool UpdateItem(string siteName, string listName, int id, IDictionary<string, object> itemUpdateData, bool batch = false)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.Web web = context.Web;
            SP.ListCollection lc = web.Lists;
            context.Load(lc);

            SP.List theList = lc.GetByTitle(listName);

            ListItem updateItem = theList.GetItemById(id);

            // We seem to have to load the item to avoid 'version conflict' errors
            context.Load(updateItem);

            var theFieldList = theList.Fields;
            context.Load(theFieldList);

            context.ExecuteQuery();

            foreach (var itemData in itemUpdateData)
            {
                var o = itemData.Value;
                if (o == null || o.GetType() == typeof(System.DBNull))
                {
                    // Leave this field in the item as default
                    continue;
                }

                var oType = o.GetType().ToString();

                // this is the column name
                string fieldName = itemData.Key;

                var theField = theFieldList.Where(p => (p.Title == fieldName) || (p.InternalName == fieldName)).FirstOrDefault();
                if (theField != null)
                {
                    fieldName = theField.InternalName;

                    string SPType = "";
                    try
                    {
                        SPType = ObjToSPDataTypeConverter[oType];

                        switch (SPType)
                        {
                            case "Text":
                                updateItem[fieldName] = o.ToString();
                                break;

                            case "Integer":
                                updateItem[fieldName] = (int)o;
                                break;

                            case "Number":
                                updateItem[fieldName] = (decimal)o;
                                break;

                            case "DateTime":
                                updateItem[fieldName] = (DateTime)o;
                                break;

                            case "ElsNull":
                                // don't do anything with nulls
                                break;

                            default:
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Misc.ReportWarning($"data for item {id}: '{fieldName}' will not be updated becuase the type '{oType}' cannot be converted");
                    }
                }
                else
                {
                    Misc.ReportWarning($"field '{fieldName}' wasn't found in list {siteName}/{listName}, so its contents were ignored.");
                }
            }

            // the "batch" switch means that the caller will take care of calling ExecuteQuery often enough to avoid throttling
            // errors
            updateItem.Update();

            if (!batch)
            {
                try
                {
                    context.ExecuteQuery();
                }
                catch( Exception ex )
                {
                    Misc.ReportWarning($"   Exception updating {siteName}/{listName}/{id} \n" +
                                 $"            {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        public static SP.ListItem GetItem(string siteName, string listName, int id)
        {
            if (!signedIn)
            {
                var sign = SignIn().Result;
            }

            ClientContext context = new ClientContext(siteName);
            context.Credentials = SPOcredentials;

            SP.List theList = context.Web.Lists.GetByTitle(listName);

            SP.ListItem theItem = theList.GetItemById(id);

            context.Load(theItem);
            context.ExecuteQuery();

            return theItem;
        }

        public static dynamic BuildDynamicFromListItem(FieldCollection fieldCollection, ListItem listItem, string originName, string originType = null, bool setIDFromGUID = true)
        {
            dynamic spDynData = SharePointOnlineInterface.ConvertItemToDynamic(fieldCollection, listItem,
                copyHidden: true, copyReadonly: true);
            if (spDynData == null)
            {
                return null;  // couldn't convert the element
            }
            // The case of this "id" is very important.  DocumentDB/CosmosDB treats this as the id
            // of the document, which is used to detemrine if it needs to be created or updated.
            // if it NOT present, CosmosDB creates a new unique id, which prevents any updating.
            // the GUID of the sharepoint item is set when it is created, therefore the document is simply
            // updated for any changes in the item.

            // also, since we're copying a lot of extra fields above, we'll copy the "ID" field that sharepoint
            // uses for the item, and that doesn't want to clash with the "id" field that CosmosDB needs

            if (setIDFromGUID)
            {
                spDynData.id = listItem["GUID"];
            }

            if ( string.IsNullOrWhiteSpace( originType ))
            {
                originType = "SPList";
            }

            spDynData.TableTypeKey = originType;

            if ( string.IsNullOrWhiteSpace( originName ))
            {
                Misc.ReportWarning($" for proper output usage, an originName is required -- this is used as a partition key");
            }

            spDynData.ListInfo = originName;

            spDynData.TableKey = originType + "-" + originName;

            return spDynData;
        }

        public static dynamic ConvertItemToDynamic( SP.FieldCollection fields , SP.ListItem item,
                              bool copyHidden = false, bool copyReadonly = false)
        {
            var returnData = new ExpandoObject() as IDictionary<string,Object>;

            foreach (var field in fields)
            {
                try
                {
                    bool copy_field = true;
                    if (!copyHidden)
                    {
                        copy_field = !field.Hidden;
                        copy_field &= field.Group != "_Hidden";
                    }

                    if (!copyReadonly)
                    {
                        copy_field &= !field.ReadOnlyField;
                    }

                    copy_field &= field.InternalName != "Attachments";
                    copy_field &= field.InternalName != "ContentType";

                    if (copy_field)
                    {
                        Object newObj = null;
                        newObj = item[field.InternalName];

                        if (newObj != null)
                        {
//                            Misc.ReportInfo($"{field.InternalName} set as {newObj.ToString()}");
                            returnData.Add(field.InternalName, newObj);
                        }
                    }
                }
                catch( Exception e )
                {
//                    Misc.ReportWarning($"problem accessing field {field.InternalName}\n {e.Message}");
                }
            }

            return returnData;
        }

#endregion

        #region Cookie Management

        // This function clears cookies from the browser control used by ADAL.
        private static void ClearCookies()
        {
            const int INTERNET_OPTION_END_BROWSER_SESSION = 42;
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_END_BROWSER_SESSION, IntPtr.Zero, 0);
        }

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int lpdwBufferLength);
#endregion

    }
}
