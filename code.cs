#region Reference(s)
using System;
using System.IO;
using System.Xml;
using System.Net;
using System.Text;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.ServiceModel;
using System.ServiceModel.Description;
using System.Web;
#endregion

namespace RFBCheckoutDashboard
{
    public partial class WebServiceTester : System.Web.UI.Page
    {
        #region Event(s)

        protected void Page_Init(object sender, EventArgs e)
        {
            Server.ScriptTimeout = 1200;
            HttpContext.Current.Response.AppendHeader("Refresh", Convert.ToString(((HttpContext.Current.Session.Timeout * 60) - 5)) + "; Url=Error.aspx?issue=timeout");
        }

        protected void Page_Load(object sender, EventArgs e)
        {
            if (!Page.IsPostBack)
            {
                if (Utils.Common.IsMobileBrowser())
                {
                    Server.Transfer("Mobile.aspx");
                }
                else
                {
                    bool enableAuth = Convert.ToBoolean(System.Configuration.ConfigurationManager.AppSettings["EnableAuthorization"]);
                    bool isAuthUser = Utils.LoginManager.IsUserAuthorizedNew("WebServiceTester.aspx");

                    if (enableAuth && !isAuthUser)
                    {
                        Server.Transfer("NotAuthorized.aspx");
                    }
                }
                //Utils.DashboardLogger.GetInstance().AddLog("Page: WebServiceTester.aspx; Logged On User: " + Utils.LoginManager.GetCurrentUser());
                Utils.Common.LogUserInfo("WSVRFBCheck", Session.SessionID);
            }
        }

        protected void btnFetchRequests_Click(object sender, EventArgs e)
        {
            txtRequest.Text = string.Empty;
            txtResponse.Text = string.Empty;
            string wsdlUrl = txtUrl.Text;
            if (wsdlUrl != null && wsdlUrl != string.Empty && wsdlUrl.IndexOf("wsdl") != -1)// && ValidateRequestURL())
            {
                lblWarning.Visible = false;
                try
                {
                    WSHttpBinding binding = new WSHttpBinding(SecurityMode.None);
                    binding.MaxReceivedMessageSize = 2147483647;
                    MetadataExchangeClient metClient = new MetadataExchangeClient(binding);
                    metClient.ResolveMetadataReferences = true;
                    MetadataSet metSet = metClient.GetMetadata(new Uri(wsdlUrl), MetadataExchangeClientMode.HttpGet);
                    WsdlImporter wsdlImporter = new WsdlImporter(metSet);
                    CodeCompileUnit codeCompileUnit = new CodeCompileUnit();
                    ServiceContractGenerator generator = new ServiceContractGenerator(codeCompileUnit);

                    Collection<ContractDescription> contracts = wsdlImporter.ImportAllContracts();
                    ServiceEndpointCollection endpoints = wsdlImporter.ImportAllEndpoints();

                    List<string> opsList = new List<string>();
                    string serviceName = string.Empty;
                    string nameSpace = string.Empty;

                    foreach (ContractDescription contract in contracts)
                    {
                        foreach (OperationDescription op in contract.Operations)
                        {
                            opsList.Add(op.Name);
                        }
                        generator.GenerateServiceContractType(contract);
                        serviceName = contract.Name;
                        nameSpace = contract.Namespace;
                        break;
                    }
                    ddlOperations.DataSource = opsList;
                    ddlOperations.DataBind();
                    ddlOperations.Visible = true;
                    lblOperations.Visible = true;

                    ViewState.Add("ServiceName", serviceName);
                    ViewState.Add("Namespace", nameSpace);

                    CodeDomProvider provider1 = CodeDomProvider.CreateProvider("CSharp");
                    StringWriter ws = new StringWriter();
                    provider1.GenerateCodeFromCompileUnit(codeCompileUnit, ws, new CodeGeneratorOptions());

                    string serverRoot = System.Configuration.ConfigurationManager.AppSettings["ServerRoot"];
                    string[] assemblyNames = new string[] { "System.Configuration.dll", "System.Xml.dll", Server.MapPath( serverRoot + "bin/") + "System.Runtime.Serialization.dll",
                        "System.dll", "System.Web.Services.dll", "System.Data.dll", Server.MapPath( serverRoot + "bin/") + "System.ServiceModel.dll" };
                    CompilerParameters options = new CompilerParameters(assemblyNames);
                    options.WarningLevel = 0;
                    options.GenerateInMemory = true;

                    string sourceCode = ws.ToString();
                    CompilerResults results = provider1.CompileAssemblyFromSource(options, sourceCode);

                    if (!results.Errors.HasErrors)
                    {
                        Assembly compiledAssembly = results.CompiledAssembly;
                        Type objClientType = compiledAssembly.GetType(serviceName);

                        foreach (MethodInfo minfo in objClientType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                        {
                            StringBuilder sb = new StringBuilder();

                            if (nameSpace != null && nameSpace != string.Empty)
                            {
                                sb.Append("<" + minfo.Name + " xmlns=\"" + nameSpace + "\">");
                            }
                            else
                            {
                                sb.Append("<" + minfo.Name + ">");
                            }

                            ParameterInfo[] paramInfos = minfo.GetParameters();
                            if (paramInfos != null && paramInfos.Length > 0)
                            {
                                foreach (ParameterInfo param in paramInfos)
                                {
                                    ConstructorInfo cons = param.ParameterType.GetConstructor(new Type[] { });
                                    if (cons != null)
                                    {
                                        if (param.ParameterType.IsSerializable)
                                        {
                                            sb.Append("<" + param.Name + ">");
                                            object obj = cons.Invoke(new object[] { });
                                            LoadPropertyMembers(obj, sb, compiledAssembly);
                                            sb.Append("</" + param.Name + ">");
                                        }
                                        else
                                        {
                                            object obj = cons.Invoke(new object[] { });
                                            LoadPropertyMembers(obj, sb, compiledAssembly);
                                        }

                                    }
                                    else
                                    {
                                        if (param.ParameterType.IsEnum)
                                        {
                                            sb.Append("<" + param.Name + ">");
                                            object obj = Activator.CreateInstance(compiledAssembly.GetType(param.ParameterType.FullName));
                                            sb.Append(obj.ToString());
                                            sb.Append("</" + param.Name + ">");
                                        }
                                        else if (param.ParameterType.IsValueType)
                                        {
                                            sb.Append("<" + param.Name + ">");
                                            switch (param.ParameterType.Name)
                                            {
                                                case "Int16":
                                                case "Int32":
                                                case "Int64":
                                                case "Double":
                                                case "Decimal":
                                                    sb.Append("0");
                                                    break;

                                                case "Boolean":
                                                    sb.Append("false");
                                                    break;

                                                case "DateTime":
                                                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd"));
                                                    break;
                                            }
                                            sb.Append("</" + param.Name + ">");
                                        }
                                        else if (param.ParameterType.IsArray)
                                        {
                                            sb.Append("<" + param.Name + ">");
                                            string propName = param.ParameterType.Name;
                                            propName = propName.Replace("[]", string.Empty);
                                            sb.Append("<" + propName + ">");
                                            sb.Append("</" + propName + ">");
                                            sb.Append("</" + param.Name + ">");
                                        }
                                        else
                                        {
                                            sb.Append("<" + param.Name + ">");
                                            sb.Append("</" + param.Name + ">");
                                        }
                                    }
                                }
                            }
                            sb.Append("</" + minfo.Name + ">");


                            string data = "<?xml version=\"1.0\" encoding=\"utf-8\"?><soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><soap:Body>";
                            data += sb.ToString();
                            data += "</soap:Body></soap:Envelope>";

                            ViewState.Add(minfo.Name + "Request", data);
                        }
                        txtRequest.Text = IndentXMLString(ViewState[ddlOperations.SelectedValue + "Request"].ToString());
                        ws.Flush();
                        ws.Close();
                    }
                    else
                    {
                        lblWarning.Text = "There was an error in loading XML requests from the assembly." + "\n" +
                            "You can still provide XML request by copy and pasting it in the Request textbox.";
                        lblWarning.Visible = true;
                        //ViewState.Clear();
                    }

                }
                catch (Exception ex)
                {
                    lblWarning.Text = "Following error occured. " + ex.Message;
                    lblWarning.Visible = true;
                    ViewState.Clear();
                    return;
                }
            }
            else
            {
                lblWarning.Text = "Service URL is either empty or incorrect. Please provide a URL with \"wsdl\" extension. Example: http://servicename.com/service.svc?wsdl";
                lblWarning.Visible = true;
                lblWarning.ForeColor = System.Drawing.Color.Red;
                txtRequest.Text = string.Empty;
                txtResponse.Text = string.Empty;
                ddlOperations.Items.Clear();
            }
        }

        protected void btnInvoke_Click(object sender, EventArgs e)
        {
            if (txtRequest.Text != string.Empty)
            {
                lblWarning.Visible = false;

                string wsdlURL = txtUrl.Text.Replace("?wsdl", string.Empty);
                string sData = txtRequest.Text;
                string soapAction = string.Empty;
                if (wsdlURL.IndexOf("asmx") != -1)
                {
                    string ns = ViewState["Namespace"].ToString();
                    if ((ns.Length - 1) != ns.LastIndexOf('/'))
                    {
                        ns += "/";
                    }
                    soapAction = ns + ddlOperations.SelectedValue;
                }
                else
                {
                    string ns = ViewState["Namespace"].ToString();
                    if ((ns.Length - 1) != ns.LastIndexOf('/'))
                    {
                        ns += "/";
                    }
                    soapAction = ns + ViewState["ServiceName"].ToString() + "/" + ddlOperations.SelectedValue;
                }
                string response = Utils.SOAPUtil.GetSOAPResponse(wsdlURL, sData, soapAction, 100);

                if (response.IndexOf("Error Occured") != -1)
                {
                    txtResponse.Text = response;
                }
                else
                {
                    txtResponse.Text = IndentXMLString(response);
                }
            }
            else
            {
                lblWarning.Text = "Request XML is not provided.";
                lblWarning.Visible = true;
                txtResponse.Text = string.Empty;
            }
        }

        protected void ddlOperations_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ViewState[ddlOperations.SelectedValue + "Request"] != null)
            {
                txtRequest.Text = IndentXMLString(ViewState[ddlOperations.SelectedValue + "Request"].ToString());
                txtResponse.Text = string.Empty;
            }
        }

        #endregion

        #region Private Method(s)

        /// <summary>
        /// This method recursively creates the request XMLs from the assembly.
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="sb"></param>
        /// <param name="dynamicAssembly"></param>
        private void LoadPropertyMembers(object obj, StringBuilder sb, Assembly dynamicAssembly)
        {
            foreach (MemberInfo mem in obj.GetType().GetMembers())
            {
                if (mem.MemberType == MemberTypes.Property)
                {
                    // This is for custom objects in the Assembly.
                    if ((((PropertyInfo)mem).PropertyType.Assembly == dynamicAssembly))
                    {
                        sb.Append("<" + mem.Name + ">");
                        if (((PropertyInfo)mem).PropertyType.IsArray)
                        {
                            System.Xml.Serialization.XmlElementAttribute[] attribs = (System.Xml.Serialization.XmlElementAttribute[])mem.GetCustomAttributes(typeof(System.Xml.Serialization.XmlElementAttribute), true);
                            if (attribs != null && attribs.Length > 0)
                            {
                                if (attribs[0].ElementName != mem.Name)
                                {
                                    string displayNode = ((PropertyInfo)mem).PropertyType.Name;
                                    displayNode = displayNode.Replace("[]", string.Empty);

                                    string propName = ((PropertyInfo)mem).PropertyType.FullName;
                                    propName = propName.Replace("[]", string.Empty);
                                    sb.Append("<" + displayNode + ">");
                                    object tmpObj = Activator.CreateInstance(dynamicAssembly.GetType(propName));
                                    LoadPropertyMembers(tmpObj, sb, dynamicAssembly);
                                    sb.Append("</" + displayNode + ">");
                                }
                                else
                                {
                                    string displayNode = ((PropertyInfo)mem).PropertyType.Name;
                                    displayNode = displayNode.Replace("[]", string.Empty);

                                    string propName = ((PropertyInfo)mem).PropertyType.FullName;
                                    propName = propName.Replace("[]", string.Empty);
                                    //sb.Append("<" + displayNode + ">");
                                    object tmpObj = Activator.CreateInstance(dynamicAssembly.GetType(propName));
                                    LoadPropertyMembers(tmpObj, sb, dynamicAssembly);
                                    //sb.Append("</" + displayNode + ">");
                                }
                            }
                            else
                            {
                                string displayNode = ((PropertyInfo)mem).PropertyType.Name;
                                displayNode = displayNode.Replace("[]", string.Empty);

                                string propName = ((PropertyInfo)mem).PropertyType.FullName;
                                propName = propName.Replace("[]", string.Empty);
                                sb.Append("<" + displayNode + ">");
                                object tmpObj = Activator.CreateInstance(dynamicAssembly.GetType(propName));
                                if (tmpObj.GetType().IsEnum)
                                {
                                    sb.Append(tmpObj.ToString());
                                }
                                else
                                {
                                    LoadPropertyMembers(tmpObj, sb, dynamicAssembly);
                                }
                                sb.Append("</" + displayNode + ">");
                            }
                        }
                        else if (((PropertyInfo)mem).PropertyType.IsClass)
                        {
                            object tmpObj = Activator.CreateInstance(dynamicAssembly.GetType(((PropertyInfo)mem).PropertyType.FullName));
                            LoadPropertyMembers(tmpObj, sb, dynamicAssembly);
                        }
                        else if (((PropertyInfo)mem).PropertyType.IsEnum)
                        {
                            FieldInfo fInfo = ((PropertyInfo)mem).PropertyType.GetFields()[1];
                            sb.Append(fInfo.Name);
                        }
                        sb.Append("</" + mem.Name + ">");
                    }
                    // This is for primitive objects in the Assembly.
                    //else if (((PropertyInfo)mem).PropertyType.IsPrimitive && ((PropertyInfo)mem).PropertyType.IsValueType)
                    else
                    {
                        if (((PropertyInfo)mem).PropertyType.IsValueType)
                        {
                            sb.Append("<" + mem.Name + ">");
                            switch (((PropertyInfo)mem).PropertyType.Name)
                            {
                                case "Int16":
                                case "Int32":
                                case "Int64":
                                case "Double":
                                case "Decimal":
                                    sb.Append("0");
                                    break;

                                case "Boolean":
                                    sb.Append("false");
                                    break;

                                case "DateTime":
                                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd"));
                                    break;
                            }
                            sb.Append("</" + mem.Name + ">");
                        }
                        else if (((PropertyInfo)mem).PropertyType.IsArray)
                        {
                            sb.Append("<" + mem.Name + ">");
                            string propName = ((PropertyInfo)mem).PropertyType.Name;
                            propName = propName.Replace("[]", string.Empty);
                            sb.Append("<" + propName + ">");
                            sb.Append("</" + propName + ">");
                            sb.Append("</" + mem.Name + ">");
                        }
                        else
                        {
                            sb.Append("<" + mem.Name + ">");
                            sb.Append("</" + mem.Name + ">");
                        }
                    }
                }
                else if (mem.MemberType == MemberTypes.Field && ((FieldInfo)mem).FieldType.IsPublic)
                {
                    if ((((FieldInfo)mem).FieldType.Assembly == dynamicAssembly))
                    {
                        //sb.Append("<" + mem.Name + ">");
                        if (((FieldInfo)mem).FieldType.IsArray)
                        {
                            string displayNode = ((FieldInfo)mem).FieldType.Name;
                            displayNode = displayNode.Replace("[]", string.Empty);

                            string propName = ((FieldInfo)mem).FieldType.FullName;
                            propName = propName.Replace("[]", string.Empty);
                            sb.Append("<" + displayNode + ">");
                            object tmpObj = Activator.CreateInstance(dynamicAssembly.GetType(propName));
                            LoadPropertyMembers(tmpObj, sb, dynamicAssembly);
                            sb.Append("</" + displayNode + ">");
                        }
                        else if (((FieldInfo)mem).FieldType.IsClass)
                        {
                            object tmpObj = Activator.CreateInstance(dynamicAssembly.GetType(((FieldInfo)mem).FieldType.FullName));
                            LoadPropertyMembers(tmpObj, sb, dynamicAssembly);
                        }
                        else if (((FieldInfo)mem).FieldType.IsEnum)
                        {
                            FieldInfo fInfo = ((FieldInfo)mem).FieldType.GetFields()[1];
                            sb.Append(fInfo.Name);
                        }
                        //sb.Append("</" + mem.Name + ">");
                    }
                    // This is for primitive objects in the Assembly.
                    //else if (((PropertyInfo)mem).PropertyType.IsPrimitive && ((PropertyInfo)mem).PropertyType.IsValueType)
                    else
                    {
                        if (((FieldInfo)mem).FieldType.IsValueType)
                        {
                            sb.Append("<" + mem.Name + ">");
                            switch (((FieldInfo)mem).FieldType.Name)
                            {
                                case "Int16":
                                case "Int32":
                                case "Int64":
                                case "Double":
                                case "Decimal":
                                    sb.Append("0");
                                    break;

                                case "Boolean":
                                    sb.Append("false");
                                    break;

                                case "DateTime":
                                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd"));
                                    break;
                            }
                            sb.Append("</" + mem.Name + ">");
                        }
                        else if (((FieldInfo)mem).FieldType.IsArray)
                        {
                            sb.Append("<" + mem.Name + ">");
                            string propName = ((FieldInfo)mem).FieldType.Name;
                            propName = propName.Replace("[]", string.Empty);
                            sb.Append("<" + propName + ">");
                            sb.Append("</" + propName + ">");
                            sb.Append("</" + mem.Name + ">");
                        }
                        else
                        {
                            sb.Append("<" + mem.Name + ">");
                            sb.Append("</" + mem.Name + ">");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// This method indents the XML requests and responses to display on the screen.
        /// </summary>
        /// <param name="xml"></param>
        /// <returns></returns>
        private string IndentXMLString(string xml)
        {
            XmlDocument doc = new XmlDocument();

            try
            {
                doc.LoadXml(xml);
                using (StringWriter buffer = new StringWriter())
                {
                    XmlWriterSettings settings = new XmlWriterSettings();
                    settings.Indent = true;

                    using (XmlWriter writer = XmlWriter.Create(buffer, settings))
                    {
                        doc.WriteTo(writer);
                        writer.Flush();
                    }

                    buffer.Flush();

                    return buffer.ToString();
                }
            }
            catch (Exception ex)
            {
                return "Error while formatting xml. Exception: " + ex.Message;
            }
        }

        #endregion
    }
}
