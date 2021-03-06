using CSR;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;

namespace BDSJSR2
{
    public class Program
    {
        static MCCSAPI mapi;

        static Hashtable shares = new Hashtable();
        static JavaScriptSerializer ser = new JavaScriptSerializer();
        static Hashtable jsfiles = new Hashtable();
        static Hashtable jsengines = new Hashtable();

        static string JSString(object o)
        {
            return o?.ToString();
        }

        #region 通用数据处理及网络功能

        delegate void LOG(object e);
        delegate string FILEREADALLTEXT(object f);
        delegate bool FILEWRITEALLTEXT(object f, object c);
        delegate bool FILEWRITELINE(object f, object c);
        delegate bool FILEEXISTS(object f);
        delegate bool FILEDELETE(object f);
        delegate bool FILECOPY(object f, object t, object o);
        delegate bool FILEMOVE(object f, object t);
        delegate bool DIREXISTS(object d);
        delegate string TIMENOW();
        delegate void SETSHAREDATA(object key, object o);
        delegate object GETSHAREDATA(object key);
        delegate object REMOVESHAREDATA(object key);
        delegate void REQUEST(object url, object mode, object param, ScriptObject f);
        delegate void SETTIMEOUT(object o, object ms);
        delegate bool MKDIR(object dirname);
        delegate string GETWORKINGPATH();
        delegate int STARTLOCALHTTPLISTEN(object port, ScriptObject f);
        delegate bool RESETLOCALHTTPLISTENER(object lisid, ScriptObject f);
        delegate bool STOPLOCALHTTPLISTEN(object lisid);

        /// <summary>
        /// 标准输出流打印消息
        /// </summary>
        static LOG cs_log = (e) =>
        {
            Console.WriteLine("" + e);
        };
        /// <summary>
        /// 文件输入流读取一个文本
        /// </summary>
        static FILEREADALLTEXT cs_fileReadAllText = (f) =>
        {
            try
            {
                return File.ReadAllText(JSString(f));
            }
            catch { }
            return null;
        };
        /// <summary>
        /// 文件输出流全新写入一个字符串
        /// </summary>
        static FILEWRITEALLTEXT cs_fileWriteAllText = (f, c) =>
        {
            try
            {
                File.WriteAllText(JSString(f), JSString(c));
                return true;
            }
            catch { }
            return false;
        };
        /// <summary>
        /// 文件输出流追加一行字符串
        /// </summary>
        static FILEWRITELINE cs_fileWriteLine = (f, c) =>
        {
            try
            {
                File.AppendAllLines(JSString(f), new string[] { JSString(c) });
                return true;
            }
            catch { }
            return false;
        };
        /// <summary>
        /// 判断文件是否存在
        /// </summary>
        static FILEEXISTS cs_fileExists = (f) =>
        {
            try
            {
                return File.Exists(JSString(f));
            }
            catch { }
            return false;
        };
        /// <summary>
        /// 删除指定文件
        /// </summary>
        static FILEDELETE cs_fileDelete = (f) =>
        {
            try
            {
                File.Delete(JSString(f));
                return true;
            }
            catch { }
            return false;
        };
        /// <summary>
        /// 复制文件
        /// </summary>
        static FILECOPY cs_fileCopy = (f, t, o) =>
        {
            try
            {
                File.Copy(JSString(f), JSString(t),bool.Parse(JSString(o)));
                return true;
            }
            catch { }
            return false;
        };
        /// <summary>
        /// 移动文件
        /// </summary>
        static FILEMOVE cs_fileMove = (f, t) =>
        {
            try
            {
                File.Move(JSString(f), JSString(t));
                return true;
            }
            catch { }
            return false;
        };
        /// <summary>
        /// 判断目录是否存在
        /// </summary>
        static DIREXISTS cs_dirExists = (d) =>
        {
            try
            {
                return Directory.Exists(JSString(d));
            }
            catch { }
            return false;
        };
        /// <summary>
        /// 返回一个当前时间的字符串
        /// </summary>
        static TIMENOW cs_TimeNow = () =>
        {
            var t = DateTime.Now;
            return t.ToString("yyyy-MM-dd HH:mm:ss");
        };
        /// <summary>
        /// 存入共享数据
        /// </summary>
        static SETSHAREDATA cs_setShareData = (k, o) =>
        {
            shares[JSString(k)] = o;
        };
        /// <summary>
        /// 获取共享数据
        /// </summary>
        static GETSHAREDATA cs_getShareData = (k) =>
        {
            return shares[JSString(k)];
        };
        /// <summary>
        /// 删除共享数据
        /// </summary>
        static REMOVESHAREDATA cs_removeShareData = (k) =>
        {
            try
            {
                var o = shares[JSString(k)];
                shares.Remove(JSString(k));
                return o;
            }
            catch { }
            return null;
        };

        // 发起一个网络请求并等待返回结果
        static string localrequest(string u, string m, string p)
        {
            string mode = m == "POST" ? m : "GET";
            string url = u;
            if (mode == "GET")
            {
                if (!string.IsNullOrEmpty(p))
                {
                    url = url + "?" + p;
                }
            }
            var req = WebRequest.Create(url);
            req.Method = mode;
            if (mode == "POST")
            {
                req.ContentType = "application/x-www-form-urlencoded;charset=UTF-8";
                if (!string.IsNullOrEmpty(p))
                {
                    byte[] payload = Encoding.UTF8.GetBytes(p);
                    req.ContentLength = payload.Length;
                    var writer = req.GetRequestStream();
                    writer.Write(payload, 0, payload.Length);
                    writer.Dispose();
                    writer.Close();
                }
            }
            var resp = req.GetResponse();
            var stream = resp.GetResponseStream();
            var reader = new StreamReader(stream); // 初始化读取器
            string result = reader.ReadToEnd();    // 读取，存储结果
            reader.Close(); // 关闭读取器，释放资源
            stream.Close();	// 关闭流，释放资源
            return result;
        }
        /// <summary>
        /// 发起一个远程HTTP请求
        /// </summary>
        static REQUEST cs_request = (u, m, p, f) =>
        {
            new Thread(() =>
            {
                string ret = null;
                try
                {
                    ret = localrequest(JSString(u), JSString(m), JSString(p));
                }
                catch { }
                if (f != null)
                {
                    try
                    {
                        f.Invoke(false, new string[] { ret });
                    }
                    catch(Exception e)
                    {
                        Console.WriteLine("[JS] File " + jsengines[f.Engine] + " Script err by call [request].");
                        if (e is ScriptEngineException ex)
                        {
                            Console.WriteLine(ex.ErrorDetails);
                        }
                    }
                }
            }).Start();
        };
        /// <summary>
        /// 延时执行一条指令
        /// </summary>
        static SETTIMEOUT cs_setTimeout = (o, ms) =>
        {
            if (o != null && ms != null)
            {
                var eng = ScriptEngine.Current;
                new Thread(() =>
                {
                    Thread.Sleep(int.Parse(JSString(ms)));
                    try
                    {
                        if (object.Equals(o.GetType(), typeof(string)))
                        {
                            eng.Execute(o.ToString());
                        }
                        else
                        {
                            var so = o as ScriptObject;
                            if (so != null)
                                so.Invoke(false);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("[JS] File " + jsengines[eng] + " Script err by call [setTimeout].");
                        if (e is ScriptEngineException ex)
                        {
                            Console.WriteLine(ex.ErrorDetails);
                        }
                    }

                }).Start();
            }
        };
        /// <summary>
        /// 创建文件夹
        /// </summary>
        static MKDIR cs_mkdir = (dirname) =>
        {
            DirectoryInfo dir = null;
            if (dirname != null)
            {
                try
                {
                    dir = Directory.CreateDirectory(JSString(dirname));
                } catch { }
            }
            return dir != null;
        };
        /// <summary>
        /// 获取工作目录
        /// </summary>
        static GETWORKINGPATH cs_getWorkingPath = () =>
        {
            return AppDomain.CurrentDomain.BaseDirectory;
        };
        // 本地侦听器
        static Hashtable httplis = new Hashtable();
        // 侦听函数
        static Hashtable httpfuncs = new Hashtable();
        // 读输入流
        static string readStream(Stream s, Encoding c)
        {
            try
            {
                var byteList = new List<byte>();
                var byteArr = new byte[2048];
                int readLen = 0;
                int len = 0;
                do
                {
                    readLen = s.Read(byteArr, 0, byteArr.Length);
                    len += readLen;
                    byteList.AddRange(byteArr);
                } while (readLen != 0);
                return c.GetString(byteList.ToArray(), 0, len);
            } catch(Exception e){ Console.WriteLine(e.StackTrace);}
            return string.Empty;
        }
        // 读url携带参数
        static ArrayList readQueryString(System.Collections.Specialized.NameValueCollection q, string url)
        {
            if (q != null)
            {
                ArrayList ol = new ArrayList();
                if (q.Count > 0)
                {
                    var d = HttpUtility.ParseQueryString(url.Substring(url.IndexOf('?')));
                    if (d != null && d.Count > 0)
                    {
                        foreach (string k in d.Keys)
                        {
                            ol.Add(new KeyValuePair<string, object>(k, d[k]));
                        }
                    }
                }
                return ol;
            }
            return null;
        }
        // 返回一个脚本监听器对应的方法构造
        static AsyncCallback makeReqCallback(ScriptObject f)
        {
            AsyncCallback cb = (x) =>
            {
                HttpListener listener = (HttpListener)x.AsyncState;
                try
                {
                    //If we are not listening this line throws a ObjectDisposedException.
                    HttpListenerContext context = listener.EndGetContext(x);
                    // 此处处理自定义方法
                    if (f != null)
                    {
                        var req = context.Request;
                        var resp = context.Response;
                        try
                        {
                            var ret = f.Invoke(false, ser.Serialize(new
                            {
                                AcceptTypes = req.AcceptTypes,
                                ContentEncoding = req.ContentEncoding,
                                ContentLength64 = req.ContentLength64,
                                ContentType = req.ContentType,
                                Cookies = req.Cookies,
                                HasEntityBody = req.HasEntityBody,
                                Headers = req.Headers,
                                HttpMethod = req.HttpMethod,
                                InputStream = readStream(req.InputStream, req.ContentEncoding),
                                IsAuthenticated = req.IsAuthenticated,
                                IsLocal = req.IsLocal,
                                IsSecureConnection = req.IsSecureConnection,
                                IsWebSocketRequest = req.IsWebSocketRequest,
                                KeepAlive = req.KeepAlive,
                                LocalEndPoint = new
                                {
                                    Address = req.LocalEndPoint.Address.ToString(),
                                    AddressFamily = req.LocalEndPoint.AddressFamily,
                                    Port = req.LocalEndPoint.Port
                                },
                                ProtocolVersion = req.ProtocolVersion,
                                QueryString = readQueryString(req.QueryString, req.RawUrl),
                                RawUrl = req.RawUrl,
                                RemoteEndPoint = new
                                {
                                    Address = req.RemoteEndPoint.Address.ToString(),
                                    AddressFamily = req.RemoteEndPoint.AddressFamily,
                                    Port = req.RemoteEndPoint.Port
                                },
                                RequestTraceIdentifier = req.RequestTraceIdentifier,
                                ServiceName = req.ServiceName,
                                TransportContext = req.TransportContext,
                                Url = req.Url,
                                UrlReferrer = req.UrlReferrer,
                                UserAgent = req.UserAgent,
                                UserHostAddress = req.UserHostAddress,
                                UserHostName = req.UserHostName,
                                UserLanguages = req.UserLanguages
                            }));
                            if (ret != null)
                            {
                                resp.ContentType = "text/plain;charset=UTF-8";//告诉客户端返回的ContentType类型为纯文本格式，编码为UTF-8
                                resp.AddHeader("Access-Control-Allow-Origin", "*");
                                resp.AddHeader("Content-type", "text/plain");//添加响应头信息
                                resp.ContentEncoding = Encoding.UTF8;
                                resp.StatusDescription = "200";//获取或设置返回给客户端的 HTTP 状态代码的文本说明。
                                resp.StatusCode = 200;// 获取或设置返回给客户端的 HTTP 状态代码。
                                var d = Encoding.UTF8.GetBytes(JSString(ret));
                                resp.OutputStream.Write(d, 0, d.Length);
                            }
                            else
                            {
                                resp.StatusDescription = "404";
                                resp.StatusCode = 404;
                            }
                        }
                        catch (Exception e)
                        {
                            if (!(e is HttpListenerException))
                            {
                                Console.WriteLine("Exception type:" + e.GetType());
                                Console.WriteLine(e.StackTrace);
                            }
                            resp.StatusDescription = "404";
                            resp.StatusCode = 404;
                        }
                    }
                    context.Response.Close();
                }
                catch (Exception e)
                {
                    if (!(e is HttpListenerException))
                    {
                        Console.WriteLine("Exception type:" + e.GetType());
                        Console.WriteLine(e.StackTrace);
                    }
                    //Intentionally not doing anything with the exception.
                    //httpfuncs.Remove(h);
                }
                var mcb = httpfuncs[listener] as AsyncCallback;
                if (mcb != null)
                    listener.BeginGetContext(mcb, listener);
            };
            return cb;
        }
        /// <summary>
        /// 开启一个本地http监听（仅限localhost和127.0.0.1访问）
        /// </summary>
        static STARTLOCALHTTPLISTEN cs_startLocalHttpListen = (port, f) =>
        {
            int hid = -1;
            try
            {
                HttpListener h = new HttpListener();
                string local1 = "localhost:";
                string local2 = "127.0.0.1:";
                string head = "http://";
                int iport = int.Parse(JSString(port));
                h.Prefixes.Add(head + local1 + iport + '/');
                h.Prefixes.Add(head + local2 + iport + '/');
                h.Start();
                AsyncCallback cb = makeReqCallback(f);
                httpfuncs[h] = cb;
                h.BeginGetContext(cb, h);
                hid = new Random().Next();
                httplis[hid] = h;
            } catch (Exception e) {
                if (!(e is HttpListenerException))
                    Console.WriteLine(e.StackTrace);
            }
            return hid;
        };
        /// <summary>
        /// 关闭一个http端口侦听
        /// </summary>
        static STOPLOCALHTTPLISTEN cs_stopLocalHttpListen = (hid) =>
        {
            var h = httplis[hid] as HttpListener;
            if (h != null)
            {
                try
                {
                    httpfuncs.Remove(h);
                    httplis.Remove(hid);
                    h.Stop();
                    return true;
                }
                catch { }
            }
            return false;
        };
        /// <summary>
        /// 重设已有侦听器的监听
        /// </summary>
        static RESETLOCALHTTPLISTENER cs_resetLocalHttpListener = (hid, f) =>
        {
            var h = httplis[hid] as HttpListener;
            if (h != null)
            {
                try
                {
                    AsyncCallback cb = makeReqCallback(f);
                    httpfuncs[h] = cb;
                    return true;
                }
                catch { }
            }
            return false;
        };
        #endregion


        // 断言商业版
        static bool assertCommercial(string fn)
        {
            if (!mapi.COMMERCIAL)
            {
                string err = string.Format("[JS] COMMERCIAL api needed by call [{0}].", fn);
                Console.WriteLine(err);
                throw new Exception(err);
            }
            return mapi.COMMERCIAL;
        }

        #region MC核心玩法相关功能
        static Hashtable beforelistens = new Hashtable();
        static Hashtable afterlistens = new Hashtable();
        delegate void ADDACTLISTENER(object k, ScriptObject f);
        delegate bool REMOVEACTLISTENER(object k, ScriptObject f);
        delegate void POSTTICK(ScriptObject f);
        delegate void SETCOMMANDDESCRIBE(object c, object s);
        delegate bool RUNCMD(object cmd);
        delegate void LOGOUT(object l);
        delegate string GETONLINEPLAYERS();
        delegate string GETSTRUCTURE(object did, object jsonposa, object jsonposb, object exent, object exblk);
        delegate bool SETSTRUCTURE(object jdata, object did, object jsonposa, object rot, object exent, object exblk);
        delegate bool SETSERVERMOTD(object motd, object isShow);
        delegate void JSERUNSCRIPT(object js, ScriptObject f);
        delegate void JSEFIRECUSTOMEVENT(object ename, object jdata, ScriptObject f);
        delegate int GETSCOREBYID(object id, object stitle);
        delegate int SETSCOREBYID(object id, object stitle, object count);
        delegate string GETMAPCOLORS(object x, object y, object z, object did);

        /// <summary>
        /// 设置事件发生前监听
        /// </summary>
        static ADDACTLISTENER cs_addBeforeActListener = (k, f) =>
        {
            ArrayList ls = (ArrayList)(beforelistens[JSString(k)] ?? new ArrayList());
            MCCSAPI.EventCab cb = x =>
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                var e = BaseEvent.getFrom(x);
                var info = ser.Serialize(e);
                bool ret = true;
                if (f != null) {
                    try
                    {
                        object oret = f.Invoke(false, new string[] { info });
                        ret = !object.Equals(oret, false);
                    }
                    catch (Exception ae)
                    {
                        Console.WriteLine("[JS] File " + jsengines[f.Engine] + " Script err by call [addBeforeActListener] [{0}].", JSString(k));
                        if (ae is ScriptEngineException aex)
                        {
                            Console.WriteLine(aex.ErrorDetails);
                        }
                    }
                }
                return ret;
            };
            mapi.addBeforeActListener(JSString(k), cb);
            var cbk = "" + Marshal.GetFunctionPointerForDelegate<MCCSAPI.EventCab>(cb);
            f.SetProperty(cbk, cbk);
            KeyValuePair<ScriptObject, MCCSAPI.EventCab> s = new KeyValuePair<ScriptObject, MCCSAPI.EventCab>(f, cb);
            ls.Add(s);
            beforelistens[JSString(k)] = ls;
        };
        /// <summary>
        /// 设置事件发生后监听
        /// </summary>
        static ADDACTLISTENER cs_addAfterActListener = (k, f) =>
        {
            ArrayList ls = (ArrayList)(afterlistens[JSString(k)] ?? new ArrayList());
            MCCSAPI.EventCab cb = x =>
            {
                var e = BaseEvent.getFrom(x);
                var info = ser.Serialize(e);
                bool ret = true;
                if (f != null)
                {
                    try
                    {
                        object oret = f.Invoke(false, new string[] { info });
                        ret = !object.Equals(oret, false);
                    }
                    catch(Exception ae)
                    {
                        Console.WriteLine("[JS] File " + jsengines[f.Engine] + " Script err by call [addAfterActListener] [{0}].", JSString(k));
                        if (ae is ScriptEngineException aex)
                        {
                            Console.WriteLine(aex.ErrorDetails);
                        }
                    }
                }
                return ret;
            };
            mapi.addAfterActListener(JSString(k), cb);
            var cbk = "" + Marshal.GetFunctionPointerForDelegate<MCCSAPI.EventCab>(cb);
            f.SetProperty(cbk, cbk);
            KeyValuePair<ScriptObject, MCCSAPI.EventCab> s = new KeyValuePair<ScriptObject, MCCSAPI.EventCab>(f, cb);
            ls.Add(s);
            afterlistens[JSString(k)] = ls;
        };

        static bool checkFuncEquals(ScriptObject a, ScriptObject b, MCCSAPI.EventCab cb)
        {
            if (a != null && b != null)
            {
                var k = "" + Marshal.GetFunctionPointerForDelegate<MCCSAPI.EventCab>(cb);
                return (a.GetProperty(k).ToString() == k) && (b.GetProperty(k).ToString() == k);
            }
            return false;
        }

        /// <summary>
        /// 移除事件发生前监听
        /// </summary>
        static REMOVEACTLISTENER cs_removeBeforeActListener = (k, f) =>
        {
            bool ret = false;
            ArrayList ls = (ArrayList)(beforelistens[JSString(k)] ?? new ArrayList());
            if (ls.Count > 0)
            {
                for (int m = ls.Count; m > 0; --m)
                {
                    var s = (KeyValuePair<ScriptObject, MCCSAPI.EventCab>)ls[m - 1];
                    if (checkFuncEquals(s.Key, f, s.Value))
                    {
                        if (mapi.removeBeforeActListener(JSString(k), s.Value))
                        {
                            f.DeleteProperty("" + Marshal.GetFunctionPointerForDelegate<MCCSAPI.EventCab>(s.Value));
                            ls.Remove(s);
                            ret = true;
                        }
                    }
                }
            }
            beforelistens[JSString(k)] = ls;
            return ret;
        };
        /// <summary>
        /// 移除事件发生后监听
        /// </summary>
        static REMOVEACTLISTENER cs_removeAfterActListener = (k, f) =>
        {
            bool ret = false;
            ArrayList ls = (ArrayList)(afterlistens[JSString(k)] ?? new ArrayList());
            if (ls.Count > 0)
            {
                for (int m = ls.Count; m > 0; --m)
                {
                    var s = (KeyValuePair<ScriptObject, MCCSAPI.EventCab>)ls[m - 1];
                    if (checkFuncEquals(s.Key, f, s.Value))
                    {
                        if (mapi.removeAfterActListener(JSString(k), s.Value))
                        {
                            f.DeleteProperty("" + Marshal.GetFunctionPointerForDelegate<MCCSAPI.EventCab>(s.Value));
                            ls.Remove(s);
                            ret = true;
                        }
                    }
                }
            }
            afterlistens[JSString(k)] = ls;
            return ret;
        };
        /// <summary>
        /// 发送一个方法至tick
        /// </summary>
        static POSTTICK cs_postTick = (f) =>
        {
            if (f != null)
                mapi.postTick(() =>
                {
                    try
                    {
                        f.Invoke(false);
                    }
                    catch (Exception ae)
                    {
                        Console.WriteLine("[JS] File " + jsengines[f.Engine] + " Script err by call [postTick].");
                        if (ae is ScriptEngineException aex)
                        {
                            Console.WriteLine(aex.ErrorDetails);
                        }
                    }
                });
        };
        /// <summary>
        /// 设置一个全局指令描述
        /// </summary>
        static SETCOMMANDDESCRIBE cs_setCommandDescribe = (c, s) =>
        {
            mapi.setCommandDescribe(JSString(c), JSString(s));
        };
        /// <summary>
        /// 执行后台指令
        /// </summary>
        static RUNCMD cs_runcmd = (cmd) =>
        {
            return mapi.runcmd(JSString(cmd));
        };
        /// <summary>
        /// 发送一条命令输出消息（可被拦截）
        /// </summary>
        static LOGOUT cs_logout = (l) =>
        {
            mapi.logout(JSString(l));
        };
        /// <summary>
        /// 获取在线玩家列表
        /// </summary>
        static GETONLINEPLAYERS cs_getOnLinePlayers = () =>
        {
            return mapi.getOnLinePlayers();
        };
        /// <summary>
        /// 获取一个结构
        /// </summary>
        static GETSTRUCTURE cs_getStructure = (did, posa, posb, exent, exblk) =>
        {
            assertCommercial("getStructure");
            return mapi.getStructure(int.Parse(JSString(did)), JSString(posa), JSString(posb), object.Equals(exent, true), object.Equals(exblk, true));
        };
        /// <summary>
        /// 设置一个结构到指定位置
        /// </summary>
        static SETSTRUCTURE cs_setStructure = (jdata, did, jsonposa, rot, exent, exblk) =>
        {
            assertCommercial("setStructure");
            return mapi.setStructure(JSString(jdata), int.Parse(JSString(did)), JSString(jsonposa), byte.Parse(JSString(rot)),
                object.Equals(exent, true), object.Equals(exblk, true));
        };
        /// <summary>
        /// 设置服务器的显示名信息<br/>
		/// （注：服务器名称加载时机在地图完成载入之后）
        /// </summary>
        static SETSERVERMOTD cs_setServerMotd = (motd, isShow) =>
        {
            return mapi.setServerMotd(JSString(motd), object.Equals(isShow, true));
        };
        /// <summary>
        /// 使用官方脚本引擎新增一段行为包脚本并执行<br/>
		/// （注：每次调用都会新增脚本环境，请避免多次重复调用此方法）
        /// </summary>
        static JSERUNSCRIPT cs_JSErunScript = (js, f) =>
        {
            MCCSAPI.JSECab p = (r) =>
            {
                f.Invoke(false, r);
            };
            mapi.JSErunScript(JSString(js), (f == null ? null : p));
        };
        /// <summary>
        /// 使用官方脚本引擎发送一个自定义事件广播（自定义事件名称不能以minecraft:开头）
        /// </summary>
        static JSEFIRECUSTOMEVENT cs_JSEfireCustomEvent = (ename, jdata, f) =>
        {
            MCCSAPI.JSECab p = (r) =>
            {
                f.Invoke(false, r);
            };
            mapi.JSEfireCustomEvent(JSString(ename), JSString(jdata), (f == null ? null : p));
        };
        /// <summary>
        /// 获取指定ID对应于计分板上的数值
        /// </summary>
        static GETSCOREBYID cs_getscoreById = (id, stitle) =>
        {
            return mapi.getscoreById(long.Parse(JSString(id)), JSString(stitle));
        };
        /// <summary>
        /// 设置指定id对应于计分板上的数值
        /// </summary>
        static SETSCOREBYID cs_setscoreById = (id, stitle, count) =>
        {
            return mapi.setscoreById(long.Parse(JSString(id)), JSString(stitle), int.Parse(JSString(count)));
        };
        /// <summary>
        /// 获取所有计分板计分项
        /// </summary>
        static GETONLINEPLAYERS cs_getAllScore = () =>
        {
            assertCommercial("getAllScore");
            return mapi.getAllScore();
        };
        /// <summary>
        /// 设置所有计分板计分项<br/>
		/// 注：设置过程会清空原有数据
        /// </summary>
        static RUNCMD cs_setAllScore = (jdata) =>
        {
            assertCommercial("setAllScore");
            return mapi.setAllScore(JSString(jdata));
        };
        /// <summary>
        /// 获取一个指定位置处区块的颜色数据<br/>
		/// 注：如区块未处于活动状态，可能返回无效颜色数据
        /// </summary>
        static GETMAPCOLORS cs_getMapColors = (x, y, z, did) =>
        {
            assertCommercial("getMapColors");
            return mapi.getMapColors(int.Parse(JSString(x)), int.Parse(JSString(y)), int.Parse(JSString(z)), int.Parse(JSString(did)));
        };
        /// <summary>
        /// 导出地图离线玩家数据
        /// </summary>
        static GETONLINEPLAYERS cs_exportPlayersData = () =>
        {
            assertCommercial("exportPlayersData");
            return mapi.exportPlayersData();
        };
        /// <summary>
        /// 导入玩家数据至地图
        /// </summary>
        static RUNCMD cs_importPlayersData = (jstr) =>
        {
            assertCommercial("importPlayersData");
            return mapi.importPlayersData(JSString(jstr));
        };
        #endregion

        #region MC玩家互动相关功能
        delegate bool RENAMEBYUUID(object uuid, object n);
        delegate string GETPLAYERABILITIES(object uuid);
        delegate bool SETPLAYERABILITIES(object uuid, object a);
        delegate bool ADDPLAYERITEM(object uuid, object id, object aux, object count);
        delegate bool SETPLAYERBOSSBAR(object uuid, object title, object per);
        delegate bool REMOVEPLAYERBOSSBAR(object uuid);
        delegate bool TRANSFERSERVER(object uuid, object addr, object port);
        delegate bool TELEPORT(object uuid, object x, object y, object z, object did);
        delegate uint SENDSIMPLEFORM(object uuid, object title, object content, object buttons);
        delegate uint SENDMODALFORM(object uuid, object title, object content, object button1, object button2);
        delegate uint SENDCUSTOMFORM(object uuid, object json);
        delegate bool RELEASEFORM(object formid);
        delegate bool SETPLAYERSIDEBAR(object uuid, object title, object list);
        delegate int GETSCOREBOARD(object uuid, object stitle);


        /// <summary>
        /// 重命名一个指定的玩家名
        /// </summary>
        static RENAMEBYUUID cs_reNameByUuid = (uuid, name) =>
        {
            return mapi.reNameByUuid(JSString(uuid), JSString(name));
        };
        /// <summary>
        /// 获取玩家能力表
        /// </summary>
        static GETPLAYERABILITIES cs_getPlayerAbilities = (uuid) =>
        {
            assertCommercial("getPlayerAbilities");
            return mapi.getPlayerAbilities(JSString(uuid));
        };
        /// <summary>
        /// 设置玩家能力表
        /// </summary>
        static SETPLAYERABILITIES cs_setPlayerAbilities = (uuid, a) =>
        {
            assertCommercial("setPlayerAbilities");
            return mapi.setPlayerAbilities(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 获取玩家属性表
        /// </summary>
        static GETPLAYERABILITIES cs_getPlayerAttributes = (uuid) =>
        {
            assertCommercial("getPlayerAttributes");
            return mapi.getPlayerAttributes(JSString(uuid));
        };
        /// <summary>
        /// 设置玩家属性临时值表
        /// </summary>
        static SETPLAYERABILITIES cs_setPlayerTempAttributes = (uuid, a) =>
        {
            assertCommercial("setPlayerTempAttributes");
            return mapi.setPlayerTempAttributes(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 获取玩家属性上限值表
        /// </summary>
        static GETPLAYERABILITIES cs_getPlayerMaxAttributes = (uuid) =>
        {
            assertCommercial("getPlayerMaxAttributes");
            return mapi.getPlayerMaxAttributes(JSString(uuid));
        };
        /// <summary>
        /// 设置玩家属性上限值表
        /// </summary>
        static SETPLAYERABILITIES cs_setPlayerMaxAttributes = (uuid, a) =>
        {
            assertCommercial("setPlayerMaxAttributes");
            return mapi.setPlayerMaxAttributes(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 获取玩家所有物品列表
        /// </summary>
        static GETPLAYERABILITIES cs_getPlayerItems = (uuid) =>
        {
            assertCommercial("getPlayerItems");
            return mapi.getPlayerItems(JSString(uuid));
        };
        /// <summary>
        /// 获取玩家当前选中项信息
        /// </summary>
        static GETPLAYERABILITIES cs_getPlayerSelectedItem = (uuid) =>
        {
            assertCommercial("getPlayerSelectedItem");
            return mapi.getPlayerSelectedItem(JSString(uuid));
        };
        /// <summary>
        /// 设置玩家所有物品列表
        /// </summary>
        static SETPLAYERABILITIES cs_setPlayerItems = (uuid, a) =>
        {
            assertCommercial("setPlayerItems");
            return mapi.setPlayerItems(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 增加玩家一个物品
        /// </summary>
        static SETPLAYERABILITIES cs_addPlayerItemEx = (uuid, a) =>
        {
            assertCommercial("addPlayerItemEx");
            return mapi.addPlayerItemEx(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 增加玩家一个物品(简易方式)
        /// </summary>
        static ADDPLAYERITEM cs_addPlayerItem = (uuid, id, aux, count) =>
        {
            return mapi.addPlayerItem(JSString(uuid), int.Parse(JSString(id)), short.Parse(JSString(aux)), byte.Parse(JSString(count)));
        };
        /// <summary>
        /// 获取玩家所有效果列表
        /// </summary>
        static GETPLAYERABILITIES cs_getPlayerEffects = (uuid) =>
        {
            assertCommercial("getPlayerEffects");
            return mapi.getPlayerEffects(JSString(uuid));
        };
        /// <summary>
        /// 设置玩家所有效果列表
        /// </summary>
        static SETPLAYERABILITIES cs_setPlayerEffects = (uuid, a) =>
        {
            assertCommercial("setPlayerEffects");
            return mapi.setPlayerEffects(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 设置玩家自定义血条
        /// </summary>
        static SETPLAYERBOSSBAR cs_setPlayerBossBar = (uuid, title, percent) =>
        {
            assertCommercial("setPlayerBossBar");
            return mapi.setPlayerBossBar(JSString(uuid), JSString(title), float.Parse(JSString(percent)));
        };
        /// <summary>
        /// 清除玩家自定义血条
        /// </summary>
        static REMOVEPLAYERBOSSBAR cs_removePlayerBossBar = (uuid) =>
        {
            assertCommercial("removePlayerBossBar");
            return mapi.removePlayerBossBar(JSString(uuid));
        };
        /// <summary>
        /// 查询在线玩家基本信息
        /// </summary>
        static GETPLAYERABILITIES cs_selectPlayer = (uuid) =>
        {
            return mapi.selectPlayer(JSString(uuid));
        };
        /// <summary>
        /// 传送玩家至指定服务器
        /// </summary>
        static TRANSFERSERVER cs_transferserver = (uuid, addr, port) =>
        {
            assertCommercial("transferserver");
            return mapi.transferserver(JSString(uuid), JSString(addr), int.Parse(JSString(port)));
        };
        /// <summary>
        /// 传送玩家至指定坐标和维度
        /// </summary>
        static TELEPORT cs_teleport = (uuid, x, y, z, did) =>
        {
            assertCommercial("teleport");
            return mapi.teleport(JSString(uuid), float.Parse(JSString(x)), float.Parse(JSString(y)), float.Parse(JSString(z)), int.Parse(JSString(did)));
        };
        /// <summary>
        /// 模拟玩家发送一个文本
        /// </summary>
        static SETPLAYERABILITIES cs_talkAs = (uuid, a) =>
        {
            return mapi.talkAs(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 模拟玩家执行一个指令
        /// </summary>
        static SETPLAYERABILITIES cs_runcmdAs = (uuid, a) =>
        {
            return mapi.runcmdAs(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 向指定的玩家发送一个简单表单
        /// </summary>
        static SENDSIMPLEFORM cs_sendSimpleForm = (uuid, title, content, buttons) =>
        {
            return mapi.sendSimpleForm(JSString(uuid), JSString(title), JSString(content), JSString(buttons));
        };
        /// <summary>
        /// 向指定的玩家发送一个模式对话框
        /// </summary>
        static SENDMODALFORM cs_sendModalForm = (uuid, title, content, button1, button2) =>
        {
            return mapi.sendModalForm(JSString(uuid), JSString(title), JSString(content), JSString(button1), JSString(button2));
        };
        /// <summary>
        /// 向指定的玩家发送一个自定义表单
        /// </summary>
        static SENDCUSTOMFORM cs_sendCustomForm = (uuid, json) =>
        {
            return mapi.sendCustomForm(JSString(uuid), JSString(json));
        };
        /// <summary>
        /// 放弃一个表单
        /// </summary>
        static RELEASEFORM cs_releaseForm = (formid) =>
        {
            return mapi.releaseForm(uint.Parse(JSString(formid)));
        };
        /// <summary>
        /// 设置玩家自定义侧边栏临时计分板
        /// </summary>
        static SETPLAYERSIDEBAR cs_setPlayerSidebar = (uuid, title, list) =>
        {
            assertCommercial("setPlayerSidebar");
            return mapi.setPlayerSidebar(JSString(uuid), JSString(title), JSString(list));
        };
        /// <summary>
        /// 清除玩家自定义侧边栏
        /// </summary>
        static REMOVEPLAYERBOSSBAR cs_removePlayerSidebar = (uuid) =>
        {
            assertCommercial("removePlayerSidebar");
            return mapi.removePlayerSidebar(JSString(uuid));
        };
        /// <summary>
        /// 获取玩家权限与游戏模式
        /// </summary>
        static GETPLAYERABILITIES cs_getPlayerPermissionAndGametype = (uuid) =>
        {
            assertCommercial("getPlayerPermissionAndGametype");
            return mapi.getPlayerPermissionAndGametype(JSString(uuid));
        };
        /// <summary>
        /// 设置玩家权限与游戏模式
        /// </summary>
        static SETPLAYERABILITIES cs_setPlayerPermissionAndGametype = (uuid, a) =>
        {
            assertCommercial("setPlayerPermissionAndGametype");
            return mapi.setPlayerPermissionAndGametype(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 断开一个玩家的连接
        /// </summary>
        static SETPLAYERABILITIES cs_disconnectClient = (uuid, a) =>
        {
            return mapi.disconnectClient(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 发送一个原始显示文本给玩家
        /// </summary>
        static SETPLAYERABILITIES cs_sendText = (uuid, a) =>
        {
            return mapi.sendText(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 获取指定玩家指定计分板上的数值<br/>
		/// 注：特定情况下会自动创建计分板
        /// </summary>
        static GETSCOREBOARD cs_getscoreboard = (uuid, a) =>
        {
            return mapi.getscoreboard(JSString(uuid), JSString(a));
        };
        /// <summary>
        /// 设置指定玩家指定计分板上的数值
        /// </summary>
        static TRANSFERSERVER cs_setscoreboard = (uuid, stitle, count) =>
        {
            return mapi.setscoreboard(JSString(uuid), JSString(stitle), int.Parse(JSString(count)));
        };
        /// <summary>
        /// 获取玩家IP
        /// </summary>
        static GETPLAYERABILITIES cs_getPlayerIP = (uuid) =>
        {
            var data = mapi.selectPlayer(JSString(uuid));
            if (!string.IsNullOrEmpty(data))
            {
                var pinfo = ser.Deserialize<Dictionary<string, object>>(data);
                if (pinfo != null)
                {
                    object pptr;
                    if (pinfo.TryGetValue("playerptr", out pptr))
                    {
                        var ptr = (IntPtr)Convert.ToInt64(pptr);
                        if (ptr != IntPtr.Zero)
                        {
                            CsPlayer p = new CsPlayer(mapi, ptr);
                            var ipport = p.IpPort;
                            var ip = ipport.Substring(0, ipport.IndexOf('|'));
                            return ip;
                        }
                    }
                }
            }
            return string.Empty;
        };
        #endregion

        static void initJsEngine(ScriptEngine eng)
        {
            eng.AddHostObject("log", cs_log);
            eng.AddHostObject("fileReadAllText", cs_fileReadAllText);
            eng.AddHostObject("fileWriteAllText", cs_fileWriteAllText);
            eng.AddHostObject("fileWriteLine", cs_fileWriteLine);
            eng.AddHostObject("fileExists", cs_fileExists);
            eng.AddHostObject("fileDelete", cs_fileDelete);
            eng.AddHostObject("fileCopy", cs_fileCopy);
            eng.AddHostObject("fileMove", cs_fileMove);
            eng.AddHostObject("dirExists", cs_dirExists);

            eng.AddHostObject("TimeNow", cs_TimeNow);
            eng.AddHostObject("setShareData", cs_setShareData);
            eng.AddHostObject("getShareData", cs_getShareData);
            eng.AddHostObject("removeShareData", cs_removeShareData);
            eng.AddHostObject("request", cs_request);
            eng.AddHostObject("setTimeout", cs_setTimeout);
            eng.AddHostObject("mkdir", cs_mkdir);
            eng.AddHostObject("getWorkingPath", cs_getWorkingPath);
            eng.AddHostObject("startLocalHttpListen", cs_startLocalHttpListen);
            eng.AddHostObject("resetLocalHttpListener", cs_resetLocalHttpListener);
            eng.AddHostObject("stopLocalHttpListen", cs_stopLocalHttpListen);

            eng.AddHostObject("addBeforeActListener", cs_addBeforeActListener);
            eng.AddHostObject("removeBeforeActListener", cs_removeBeforeActListener);
            eng.AddHostObject("addAfterActListener", cs_addAfterActListener);
            eng.AddHostObject("removeAfterActListener", cs_removeAfterActListener);
            eng.AddHostObject("postTick", cs_postTick);
            eng.AddHostObject("setCommandDescribe", cs_setCommandDescribe);
            eng.AddHostObject("runcmd", cs_runcmd);
            eng.AddHostObject("logout", cs_logout);
            eng.AddHostObject("getOnLinePlayers", cs_getOnLinePlayers);
            eng.AddHostObject("getStructure", cs_getStructure);
            eng.AddHostObject("setStructure", cs_setStructure);
            eng.AddHostObject("setServerMotd", cs_setServerMotd);
            eng.AddHostObject("JSErunScript", cs_JSErunScript);
            eng.AddHostObject("JSEfireCustomEvent", cs_JSEfireCustomEvent);
            eng.AddHostObject("getscoreById", cs_getscoreById);
            eng.AddHostObject("setscoreById", cs_setscoreById);
            eng.AddHostObject("getAllScore", cs_getAllScore);
            eng.AddHostObject("setAllScore", cs_setAllScore);
            eng.AddHostObject("getMapColors", cs_getMapColors);
            eng.AddHostObject("exportPlayersData", cs_exportPlayersData);
            eng.AddHostObject("importPlayersData", cs_importPlayersData);

            eng.AddHostObject("reNameByUuid", cs_reNameByUuid);
            eng.AddHostObject("getPlayerAbilities", cs_getPlayerAbilities);
            eng.AddHostObject("setPlayerAbilities", cs_setPlayerAbilities);
            eng.AddHostObject("getPlayerAttributes", cs_getPlayerAttributes);
            eng.AddHostObject("setPlayerTempAttributes", cs_setPlayerTempAttributes);
            eng.AddHostObject("getPlayerMaxAttributes", cs_getPlayerMaxAttributes);
            eng.AddHostObject("setPlayerMaxAttributes", cs_setPlayerMaxAttributes);
            eng.AddHostObject("getPlayerItems", cs_getPlayerItems);
            eng.AddHostObject("getPlayerSelectedItem", cs_getPlayerSelectedItem);
            eng.AddHostObject("setPlayerItems", cs_setPlayerItems);
            eng.AddHostObject("addPlayerItemEx", cs_addPlayerItemEx);
            eng.AddHostObject("addPlayerItem", cs_addPlayerItem);
            eng.AddHostObject("getPlayerEffects", cs_getPlayerEffects);
            eng.AddHostObject("setPlayerEffects", cs_setPlayerEffects);
            eng.AddHostObject("setPlayerBossBar", cs_setPlayerBossBar);
            eng.AddHostObject("removePlayerBossBar", cs_removePlayerBossBar);
            eng.AddHostObject("selectPlayer", cs_selectPlayer);
            eng.AddHostObject("transferserver", cs_transferserver);
            eng.AddHostObject("teleport", cs_teleport);
            eng.AddHostObject("talkAs", cs_talkAs);
            eng.AddHostObject("runcmdAs", cs_runcmdAs);
            eng.AddHostObject("sendSimpleForm", cs_sendSimpleForm);
            eng.AddHostObject("sendModalForm", cs_sendModalForm);
            eng.AddHostObject("sendCustomForm", cs_sendCustomForm);
            eng.AddHostObject("releaseForm", cs_releaseForm);
            eng.AddHostObject("setPlayerSidebar", cs_setPlayerSidebar);
            eng.AddHostObject("removePlayerSidebar", cs_removePlayerSidebar);
            eng.AddHostObject("getPlayerPermissionAndGametype", cs_getPlayerPermissionAndGametype);
            eng.AddHostObject("setPlayerPermissionAndGametype", cs_setPlayerPermissionAndGametype);
            eng.AddHostObject("disconnectClient", cs_disconnectClient);
            eng.AddHostObject("sendText", cs_sendText);
            eng.AddHostObject("getscoreboard", cs_getscoreboard);
            eng.AddHostObject("setscoreboard", cs_setscoreboard);
            eng.AddHostObject("getPlayerIP", cs_getPlayerIP);
        }

        [DllImport("Kernel32.dll")]
        static extern int GetPrivateProfileStringA(
            string lpAppName,
            string lpKeyName,
            string lpDefault,
            byte[] lpReturnedString,
            int nSize,
            string lpFileName);
        [DllImport("Kernel32.dll")]
        static extern uint WritePrivateProfileStringA(
            string lpAppName,
            string lpKeyName,
            string lpString,
            string lpFileName);

        /// <summary>
        /// JSR初始化
        /// </summary>
        /// <param name="api"></param>
        public static void init(MCCSAPI api)
        {
            mapi = api;
            string plugins = "plugins/";
            string settingdir = plugins + "settings/";          // 固定配置文件目录 - plugins/settings
            string settingpath = settingdir + "netjs.ini";      // 固定配置文件 - netjs.ini
            string JSPATH = "";
            var path = new byte[256];
            int len = 0;
            if ((len = GetPrivateProfileStringA("NETJS", "jsdir", null, path, 256, settingpath)) < 1)
            {
                Console.WriteLine("[JSR] 未能读取插件库配置文件，使用默认配置[详见" + settingpath +
                    "]");
                JSPATH = "NETJS";
                try
                {
                    Directory.CreateDirectory(plugins);
                    Directory.CreateDirectory(settingdir);
                    WritePrivateProfileStringA("NETJS", "jsdir", JSPATH, settingpath);
                }
                catch { }
            } else
                JSPATH = Encoding.UTF8.GetString(path, 0, len);
            // 此处装载所有js文件
            try
            {
                if (!Directory.Exists(JSPATH))
                {
                    Console.WriteLine("[JSR] 未检测到js插件库。请将js文件放置入" + JSPATH + "文件夹内。[配置详见" + settingpath + "]");
                    return;
                }
                string[] jss = Directory.GetFiles(JSPATH, "*.js");
                if (jss.Length > 0)
                {
                    // 初始化JS引擎
                    foreach (string n in jss)
                    {
                        jsfiles[n] = File.ReadAllText(n);
                        Console.WriteLine("[JSR] 读取 " + n);
                    }
                    foreach (string n in jss)
                    {
                        var eng = new V8ScriptEngine();
                        initJsEngine(eng);
                        jsengines[eng] = n;
                        try
                        {
                            eng.Execute(jsfiles[n].ToString());
                        } catch(Exception e)
                        {
                            Console.WriteLine("[JS] File " + n + " Script err by loading [runtime].");
                            if (e is ScriptEngineException ex)
                            {
                                Console.WriteLine(ex.ErrorDetails);
                            }
                        }
                    }
                }
            }catch(Exception e)
            {
                Console.WriteLine(e.StackTrace);
            }
        }
    }
}

namespace CSR
{
    partial class Plugin
    {
        /// <summary>
        /// 通用调用接口，需用户自行实现
        /// </summary>
        /// <param name="api">MC相关调用方法</param>
        public static void onStart(MCCSAPI api)
        {
            Console.WriteLine("[JSR] Net版JS加载平台已装载。");
            // TODO 此接口为必要实现
            BDSJSR2.Program.init(api);
        }
    }
}
