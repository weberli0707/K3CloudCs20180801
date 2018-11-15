﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;

namespace K3CloudCs
{
    public class Edb_OUTSTOCK_DAL
    {
        private string strcon;
        string date_type = "";
        string page_size = "";
        DataTable dtTrade;
        DataTable dtTradeEntry;
        ConfigHelper config;
        public Edb_OUTSTOCK_DAL(string Constring,string strpage_size, string strdate_type)
        {
            config = new ConfigHelper();
            strcon = Constring;
            dtTrade = new DataTable();
            string TradeFields = ConfigurationManager.AppSettings["TradeFields"];
            string TradeValues = ConfigurationManager.AppSettings["TradeDefaultValues"];
            string[] Fields = TradeFields.Split(',');
            string[] Values = TradeValues.Split(',');
            if (Fields.Length > 0 && Values.Length > 0 && Fields.Length == Values.Length)
            {
                CreateTable(dtTrade, Fields, Values);
            }
            dtTradeEntry = new DataTable();
            string TradeEntryFields = ConfigurationManager.AppSettings["TradeEntryFields"];
            string TradeEntryValues = ConfigurationManager.AppSettings["TradeEntryDefaultValues"];
            string[] EntryFields = TradeFields.Split(',');
            string[] EntryValues = TradeValues.Split(',');
            if (EntryFields.Length > 0 && EntryValues.Length > 0 && EntryFields.Length == EntryValues.Length)
            {
                CreateTable(dtTradeEntry, EntryFields, EntryValues);
            }
            date_type = config.ReadValueByKey(ConfigurationFile.AppConfig, "date_type");
            page_size = config.ReadValueByKey(ConfigurationFile.AppConfig, "page_size");
        }
        private void SqlBulkCopyInsert(string strTableName, DataTable dtData)
        {
            try
            {
                using (SqlBulkCopy sqlRevdBulkCopy = new SqlBulkCopy(strcon))//引用SqlBulkCopy  
                {
                    sqlRevdBulkCopy.DestinationTableName = strTableName;//数据库中对应的表名  
                    sqlRevdBulkCopy.NotifyAfter = dtData.Rows.Count;//有几行数据  
                    sqlRevdBulkCopy.WriteToServer(dtData);//数据导入数据库  
                    sqlRevdBulkCopy.Close();//关闭连接  
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteFileLog(typeof(Edb_OUTSTOCK_DAL), ex.ToString());
            }
        }	
	    private void StringToDB(string begin_time,string end_time,bool flag)
	    {
            List<SqlParameter> pars = new List<SqlParameter>();            
            pars.Add(new SqlParameter("@begin_time",begin_time));
            pars.Add(new SqlParameter("@end_time", end_time));
            pars.Add(new SqlParameter("@flag", flag));
            int i = SqlHelper.ExecuteNonQuery(strcon, CommandType.Text, "Delete from tb_JsonStream where flag=1 or (begin_time=@begin_time and end_time=@end_time);Insert Into tb_JsonStream(begin_time,end_time,flag) values (@begin_time,@end_time,@flag)", pars.ToArray());

	    }
        private void CreateTable(DataTable dt, string[] Fileds, string[] Values)
        {
            for (int i = 0; i < Fileds.Length; i++)
            {
                DataColumn Column = new DataColumn();
                //该列的数据类型
                Column.DataType = Type.GetType("System.String");
                //该列得名称
                Column.ColumnName = Fileds[i];
                //该列得默认值
                Column.DefaultValue = Values[i];

                dt.Columns.Add(Column);
            }
        }
        private DataTable JsonToDataTable(string sJson)
        {
            try
            {
                DataTable api_Table = JsonConvert.DeserializeObject(sJson, typeof(DataTable)) as DataTable;
                return api_Table;
            }
            catch
            {
                return null;
            }
        }
        private string GetJson(DataTable dt)
        {
            try
            {
                string sJson = JsonConvert.SerializeObject(dt);
                return sJson;
            }
            catch
            {
                return string.Empty;
            }
        }
        public void DownloadErrorOutToDB(string StrserviceUrl, string Strdbhost, string Strappkey, string Strappscret, string Strtoken)
        {
            object o = SqlHelper.ExecuteScalar(strcon, CommandType.Text, "select count(id) from tb_JsonStream where flag=0");
            if (o != null)
            {
                using (SqlDataReader sdr = SqlHelper.ExecuteReader(strcon, CommandType.Text, "select * from (select distinct begin_time,end_time from tb_JsonStream where flag=0 ) t order by begin_time"))
                {
                    while (sdr.Read())
                    {
                        string begin_time = sdr["begin_time"].ToString();
                        string end_time = sdr["end_time"].ToString();
                        LoadInfo(date_type, begin_time, end_time,"未发货", StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);
                        LoadInfo(date_type, begin_time, end_time,"已发货", StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);
                    }
                }                
            }
        }
        public void DownloadOutToDB(string StrserviceUrl, string Strdbhost, string Strappkey, string Strappscret, string Strtoken)
        {
            object strdt1 = SqlHelper.ExecuteScalar(strcon, CommandType.Text, "select top 1 tid_time from tb_trade order by tid_time desc");
            try
            {
                DateTime dt1;
                if (strdt1 != null)
                {
                    dt1 = Convert.ToDateTime(strdt1.ToString());                    
                }
                else
                {
                    dt1 = DateTime.Now.Date.AddYears(-1);
                }
                TimeSpan span = DateTime.Now- dt1;
                if (span.TotalHours> 1)
                {
                    DateTime dt2 = dt1.AddDays(1);
                    LoadInfo(date_type, dt1.ToString("yyyy-MM-dd HH:mm:ss"), dt2.ToString("yyyy-MM-dd HH:mm:ss"),"未发货", StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);
                    LoadInfo(date_type, dt1.ToString("yyyy-MM-dd HH:mm:ss"), dt2.ToString("yyyy-MM-dd HH:mm:ss"),"已发货", StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);                   
                }
            }               
            catch(Exception ex)
            {
                LogHelper.WriteFileLog(typeof(Edb_OUTSTOCK_DAL), ex.ToString());
            }
        } 
        private void LoadInfo(string date_type,string begin_time,string end_time,string order_status, string StrserviceUrl, string Strdbhost, string Strappkey, string Strappscret, string Strtoken)
        {
            bool isSuccess = false;
            string strJson = string.Empty;
            while (isSuccess != true)
            {
                EdbLib edb = new EdbLib(StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);
                Dictionary<String, String> @params = edb.edbGetCommonParams("edbTradeGet");
                if (date_type != "") @params["date_type"] = date_type;
                @params["begin_time"] = begin_time;
                @params["end_time"] = end_time;
                @params["order_status"] = order_status;
                @params["page_no"] = "1";
                @params["page_size"] = page_size;
                strJson = edb.edbRequstPost(@params, out isSuccess);
                edb = null;
            }

            if (strJson != string.Empty)
            {
                try
                {
                    JObject jo = (JObject)JsonConvert.DeserializeObject(strJson);
                    JArray arr = (JArray)jo["Success"]["items"]["item"];
                    int total_results = int.Parse(jo["Success"]["total_results"].ToString());
                    if (total_results > 0)
                    {
                        DateTime dt = DateTime.Parse(begin_time);
                        JArray narr = arr;
                        JArray j = null;
                        for (int x = arr.Count - 1; x >= 0; x--)
                        {
                            JObject obj = (JObject)arr[x];
                            if (total_results == int.Parse(page_size))
                            {
                                string tid_time = obj["tid_time"].ToString();
                                if (dt < DateTime.Parse(tid_time))
                                {
                                    dt = DateTime.Parse(tid_time);
                                }
                            }
                            if (IsExists(obj["tid"].ToString()) == false)
                            {
                                if (j != null)
                                {
                                    //j.Merge((JArray)obj["tid_item"]);
                                    foreach (JObject job in (JArray)obj["tid_item"])
                                    {
                                        j.Add(job);
                                    }
                                }
                                else
                                {
                                    j = (JArray)obj["tid_item"];
                                }
                                obj.Remove("tid_item");
                            }
                            else
                            {
                                arr.Remove(obj);
                            }
                        }
                        if (arr != null && j != null)
                        {
                            dtTrade = JsonToDataTable(arr.ToString());
                            SqlBulkCopyInsert("tb_Trade", dtTrade);
                            dtTradeEntry = JsonToDataTable(j.ToString());
                            SqlBulkCopyInsert("tb_TradeEntry", dtTradeEntry);
                        }
                        StringToDB(begin_time, end_time, true);
                        if (total_results == int.Parse(page_size) && dt < DateTime.Parse(end_time) && dt > DateTime.Parse(begin_time))
                        {
                            LoadInfo(date_type, dt.ToString("yyyy-MM-dd HH:mm:ss"), end_time, order_status, StrserviceUrl, Strdbhost, Strappkey, Strappscret, Strtoken);
                        }
                    }
                    else
                    {
                        StringToDB(begin_time, end_time, true);
                    }

                }
                catch
                {
                    LogHelper.WriteFileLog(typeof(Edb_OUTSTOCK_DAL), strJson);
                }
            }
            else
            {
                StringToDB(begin_time, end_time, false);
            }
        }        
        private bool IsExists(string strBillNO)
        {            
            bool isEx = false;
            object o = SqlHelper.ExecuteScalar(strcon, CommandType.Text, "select tid from tb_Trade where tid='" + strBillNO + "'");
            if (o != null)
            {
                isEx = true;
            }
            return isEx;
        }
    }
}
