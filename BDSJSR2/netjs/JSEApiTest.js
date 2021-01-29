// 载入addon脚本，包含功能：LOG开启；注册互动功能用途的自定义消息监听器（日志, 临时脚本, 指令）
function loadAddjs() {
	let js = "var system = server.registerSystem(0, 0);\n\
		let logs = system.createEventData('minecraft:script_logger_config');\n\
        logs.data.log_information = true;\n\
        logs.data.log_errors = true;\n\
        logs.data.log_warnings = true;\n\
        system.broadcastEvent('minecraft:script_logger_config', logs);\n\
		system.listenForEvent('mytest:testevent', (e)=>{console.log('我将输出' + e.data);});\n\
		system.listenForEvent('CSRCall:runscript', (e)=>{console.log('' + eval(e.data));});\n\
		system.listenForEvent('CSRCall:runcmd', (e)=>{system.executeCommand(e.data, (ccb)=>{console.log(ccb);});});\n\
		";
	log('脚本内容=' + js);
	JSErunScript(js, (r)=>{log("执行结果=" + r);});
}

// 引擎初始化监听
addAfterActListener("onScriptEngineInit", function(e){
	log("[JSR] 官方脚本引擎已启动，即将开启联动任务");
	// 延时3秒，载入addon js脚本
	setTimeout(loadAddjs, 3000);
	// 延时4秒，发送log消息测试
	setTimeout(function(){
		JSEfireCustomEvent("mytest:testevent", '一个文本', (r)=>{
			if (r)
				log("自定义事件信息发送成功。");
		});
	}, 4000);
	// 延时5秒，发送临时脚本测试
	setTimeout(function () {
		JSEfireCustomEvent("CSRCall:runscript", 'system.executeCommand("/me 123", null);', (r) => {
			if (r)
				log("自定义临时脚本发送成功。");
		});
	}, 5000);
	// 延时6秒，发送引擎指令测试
	setTimeout(function () {
		JSEfireCustomEvent("CSRCall:runcmd", '/tpa x', (r) => {
			if (r)
				log("自定义指令发送成功。");
		});
	}, 6000);
});

// 引擎接收日志监听
addAfterActListener('onScriptEngineLog', function(e) {
	let je = JSON.parse(e);
	log('[JSR] 来自官方脚本引擎的LOG，内容=' + je.log);
});

// 引擎执行指令监听
addBeforeActListener('onScriptEngineCmd', function (e) {
	let je = JSON.parse(e);
	log('[JSR] 来自官方脚本引擎的指令，内容=' + je.cmd);
	if (je.cmd.indexOf('/tpa') == 0) {	// 特定的插件指令处理，拦截
		return false;
    }
});

log('[JSEApiTest] 官方引擎交互测试脚本已加载。');