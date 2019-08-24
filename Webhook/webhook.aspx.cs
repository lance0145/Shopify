using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using System.IO;
using System.Web.Script.Serialization;
using System.Web.Configuration;
using Payments.Shopify.Models;
using System.Net;

namespace Payments.Shopify.Webhook
{
    public partial class webhook : Classes.Exceptions
    {
        public static string BrandName = WebConfigurationManager.AppSettings["BrandName"];
        public static string PaymentsLink = WebConfigurationManager.AppSettings["PaymentsLink"];

        protected void Page_Load(object sender, EventArgs e)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            string json = string.Empty;
            Stream request = Request.InputStream;
            request.Seek(0, System.IO.SeekOrigin.Begin);
            json = new StreamReader(request).ReadToEnd();
            string content = "";

            #region log
            string path = @"C:\Websites\Payments\Logs\WithPsp\" + DateTime.Now.ToString("yyyy.MM.dd") + ".txt";
            try
            {
                content = "";

                {

                }

                content += "\r\n =======================================================================\r\n";
                content += "-----Got From Shopify Webhook-----\r\n";
                content += "Form: " + HttpUtility.UrlDecode(Request.Form.ToString()) + " \r\n";
                content += "QueryString: " + HttpUtility.UrlDecode(Request.QueryString.ToString()) + " \r\n";
                content += "Headers: " + HttpUtility.UrlDecode(Request.Headers.ToString()) + " \r\n";
                content += "Json: " + json + " \r\n";

                content += "\r\n \r\n";

                File.AppendAllText(path, content);
            }
            catch { }

            #endregion

            if (!string.IsNullOrEmpty(json))
            {
                #region Handling the Json and Deserializing it into Object
                var ShopResponse = ser.Deserialize<ShopModel>(json);
                #endregion

                if (ShopResponse != null)
                {
                    //Check if Request is financial_status == "paid"

                    if (ShopResponse.financial_status.ToLower() == "paid")
                    {

                        #region Validate and Fix Phone Number
                        //Perform Lookup To get Country Info via Country Name
                        var CountryInfo = Classes.countries.RestGetCountryInfo(ShopResponse.customer.default_address.country);
                        string client_phone = string.Empty;

                        //Fix and Format the Clients Phone Number
                        client_phone = ShopResponse.customer.phone?.ToString().Replace(" ", "").Replace("+", "") ?? ShopResponse.customer.default_address.phone.ToString().Replace(" ", "").Replace("+", "");
                        
                        //Check if Country Info is not null
                        if (CountryInfo != null)
                        {
                            //Get Correct Country Code
                            var CountryCode = CountryInfo.FirstOrDefault().callingCodes[0].ToString();
                            //Get Country Code Length
                            int ccLength = CountryCode.Length;

                            //Extract the CC value on the clients Phone Number
                            string temp_num_cc = client_phone.Substring(0, ccLength);

                            //Check if on Temp CC value not Contains the Country Code, and if Not add The Correct CC Code
                            if (!temp_num_cc.Contains(CountryCode))
                            {
                                client_phone = string.Concat(CountryCode, client_phone);
                            }
                        }


                        #endregion

                        #region Build Proper Client Object
                        Classes.BrandsMaster.CRM.Builders.Client.ClientObject client = new Classes.BrandsMaster.CRM.Builders.Client.ClientObject
                        {
                            actual_client_ip = null,
                            client_ip = null,
                            Fname = ShopResponse.customer.first_name,
                            Lname = ShopResponse.customer.last_name,
                            email = ShopResponse.email,
                            Phone = client_phone,
                            pool = null,
                            deposit_amount = 0,
                            withdrawal_amount = 0,
                            Dob = null,
                            country = ShopResponse.customer.default_address.country ?? null,
                            state = null,
                            address1 =  null,
                            address2 =  null,
                            city =  null,
                            zip_code =  null,
                            verified = "NO",
                            Referral = "ProfitClass",
                            MTInitialCurrency = Classes.MT.MT5.Builders.Objects.MTCurrencies.USD,
                            site_language = int.Parse("0"),
                            UpdatedDate = DateTime.UtcNow,
                            UpdatedBy = "0",
                            status = "New",
                            MTtype = Classes.BrandsMaster.CRM.Builders.Objects.MTTypes.Live,
                            CreateSiteAccess = true,
                            AgreedtoLegalDocs = true,
                            entry_type = Classes.BrandsMaster.CRM.Builders.Client.ClientEnteryType.Lead
                        };
                        #endregion


                        var createRes = Classes.BrandsMaster.CRM.Create.CreateClient(client);

                        //Validate Create Client Response and Do Next Step After Validating it

                        if (createRes.Success == true)
                        {

                            //If success true we need to Create Deposit For the Client.

                            #region GET CLIENT INFO (TA,ID)
                            string TA_Login = string.Empty;
                            string TA_Currency = string.Empty;
                            string PIN = string.Empty;
                            var TA = Utilities.Helpers.GetClientTaInfo(createRes.ClientInfoObject.Id.ToString());
                            if (TA != null)
                            {
                                TA_Login = TA.Rows[0]["login"].ToString();
                                TA_Currency = TA.Rows[0]["currency"].ToString();
                                PIN = TA.Rows[0]["id"].ToString();
                            }
                            #endregion

                            #region Build Transaction Object

                            Classes.BrandsMaster.CRM.Builders.Objects.Transaction Transaction = new Classes.BrandsMaster.CRM.Builders.Objects.Transaction();

                            Transaction = new Classes.BrandsMaster.CRM.Builders.Objects.Transaction()
                            {
                                psp_transaction_id = !string.IsNullOrEmpty(ShopResponse.order_number.ToString()) ? ShopResponse.order_number.ToString() : ShopResponse.number.ToString(),
                                amount = string.IsNullOrEmpty(ShopResponse.total_price) ? 0 : Convert.ToDouble(ShopResponse.total_price),
                                created_date = DateTime.UtcNow,
                                currency = (Classes.MT.MT5.Builders.Objects.MTCurrencies)System.Enum.Parse(typeof(Classes.MT.MT5.Builders.Objects.MTCurrencies), ShopResponse.currency),
                                fundprocessor_id = int.Parse("26"),
                                isfake = false,
                                psp_id = int.Parse("71"),
                                status = Classes.BrandsMaster.CRM.Builders.Objects.TransactionStatuses.Approved,
                                sync_with_mt = true,
                                ta_id = string.IsNullOrEmpty(PIN) ? 0 : int.Parse(PIN),
                                ta_login = TA_Login == "-1" ? (int?)null : int.Parse(TA_Login),
                                client_id = int.Parse(createRes.ClientInfoObject.Id.ToString()),
                                Type = Classes.BrandsMaster.CRM.Builders.Objects.TransactionType.Deposit,
                                UpdatedBy = "System-Shoppify(Webhook)",
                                Convert_To = (Classes.MT.MT5.Builders.Objects.MTCurrencies)System.Enum.Parse(typeof(Classes.MT.MT5.Builders.Objects.MTCurrencies), TA_Currency),
                                note = $"Approved ({ShopResponse.financial_status})",
                                card_number = ShopResponse.payment_details.credit_card_number ?? null,
                                card_holder = string.Concat(ShopResponse.customer.first_name, " ", ShopResponse.customer.last_name),
                                created_date_psp_time = DateTime.UtcNow

                            };
                            #endregion

                            #region Create the Transaction
                            if (ShopResponse.financial_status.ToLower() == "paid")
                            {
                                int counter = 0;
                                while (0 == 0)
                                {
                                    content = $"CreateTransaction Start ({counter}) \r\n";
                                    File.AppendAllText(path, content);
                                    try
                                    {
                                        Transaction.id = Classes.BrandsMaster.CRM.Create.CreateTransaction(Transaction);
                                        content = $"CreateTransaction Success ({Transaction.id}) \r\n";
                                        File.AppendAllText(path, content);
                                        break;
                                    }
                                    catch (Exception x)
                                    {
                                        content = $"CreateTransaction Error ({x.Message}) \r\n";
                                        File.AppendAllText(path, content);
                                        counter++;
                                    }
                                    if (counter > 5)
                                    {
                                        Transaction.id = null;
                                        break;
                                    }
                                }
                                content = "CreateTransaction End \r\n";
                                File.AppendAllText(path, content);
                            }

                            #endregion

                            #region log
                            path = @"C:\Websites\Payments\Logs\WithPsp\" + DateTime.Now.ToString("yyyy.MM.dd") + ".txt";

                            try
                            {
                                content = "";

                                {

                                }

                                content += "\r\n =======================================================================\r\n";
                                content += "-----CRM AFTER UPDATE-----\r\n";
                                content += "Form: " + HttpUtility.UrlDecode(Request.Form.ToString()) + " \r\n";
                                content += "Transaction JSON: " + ser.Serialize(Transaction) + " \r\n";
                                content += "Transaction ID: " + Transaction.id + " \r\n";
                                content += "Date: " + DateTime.UtcNow + " \r\n";
                                content += "\r\n =======================================================================\r\n";
                                content += "\r\n \r\n";

                                File.AppendAllText(path, content);
                            }
                            catch { }
                            #endregion

                        }
                        else
                        {
                            //Log the Create Response and Terminate the Process
                            #region Log Failed Create Response
                            path = @"C:\Websites\Payments\Logs\WithPsp\" + DateTime.Now.ToString("yyyy.MM.dd") + ".txt";
                            try
                            {
                                content = "";

                                {

                                }

                                content += "\r\n =======================================================================\r\n";
                                content += $"-----Create Attempt Request Logs-----\r\n";
                                content += "Response: " + ser.Serialize(createRes) + " \r\n";
                                content += "Message: " + createRes.Message + " \r\n";
                                content += "Date: " + DateTime.UtcNow + " \r\n";
                                content += "\r\n =======================================================================\r\n";
                                content += "\r\n \r\n";

                                File.AppendAllText(path, content);
                            }
                            catch { }
                            #endregion
                        }

                    }
                    else
                    {
                        //just draft the request to logs to have details on the request

                        #region Log for other request
                        path = @"C:\Websites\Payments\Logs\WithPsp\" + DateTime.Now.ToString("yyyy.MM.dd") + ".txt";
                        try
                        {
                            content = "";

                            {

                            }

                            content += "\r\n =======================================================================\r\n";
                            content += $"-----Shopify Webhook Attempt Request Logs-----\r\n";
                            content += "json: " + ser.Serialize(ShopResponse) + " \r\n";
                            content += "Date: " + DateTime.UtcNow + " \r\n";
                            content += "\r\n =======================================================================\r\n";
                            content += "\r\n \r\n";

                            File.AppendAllText(path, content);
                        }
                        catch { }
                        #endregion
                    }
                }

            }

        }
    }
}
