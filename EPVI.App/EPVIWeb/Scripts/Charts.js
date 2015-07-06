var EPVICharts = EPVICharts || {}; // namespace

EPVICharts.Chart = (function () {
    var proto = Chart.prototype;
    
    function Chart($view) {
        var self = this;
        this.$view = $view;

        EPVICharts.Resources = $view.data("chart-resources");

        this.dataPool = new EPVICharts.Data.Pool();
        this.dataLoader = new EPVICharts.Data.Loader(this.dataPool);
        this.chartBlocks = initChartBlocks(this, $view);
        this.isAnchored = false;
        this.searchDlg = null;
        this.saveDisplayIntervalTimeout = null;
        this.suppressSaveDisplayInterval = false;
        this.selectedItems = [];

        initCommands(this);

        $(window).resize(function () {
            self.resize();
        });

        this.resize();

        //Tooltip initialisieren
//        $(document).tooltip();

        // Daten etwas verzoegert laden
        window.setTimeout(function () {
            self.loadData();
        }, 10);
    }

    proto.initChartBlock = initChartBlock;
    proto.resize = resize;
    proto.loadData = loadData;
    proto.loadFunctionData = loadFunctionData;
    proto.updateZoomCommands = updateZoomCommands;
    proto.updateBlockCommands = updateBlockCommands;
    proto.setupBlockCommands = setupBlockCommands;
    proto.revertZoom = revertZoom;
    proto.navToConfig = navToConfig;
    proto.packChartParams = packChartParams;
    proto.editTimes = editTimes;
    proto.openSearch = openSearch;
    proto.hasMultiChartBlocks = hasMultiChartBlocks;
    proto.addChartBlock = addChartBlock;
    proto.removeChartBlock = removeChartBlock;
    proto.syncChartBlock = syncChartBlock;
    proto.toggleAnchorChartBlocks = toggleAnchorChartBlocks;
    proto.setAnchorChartBlocks = setAnchorChartBlocks;
    proto.notifySyncAnchorChartBlock = notifySyncAnchorChartBlock;
    proto.findFunc = findFunc;
    proto.delaySaveDisplayInterval = delaySaveDisplayInterval;
    proto.showSelInfo = showSelInfo;

    function initChartBlocks(chart) {
        var blocks = [];
        chart.$view.find(".chart-block").each(function () {
            var block = new EPVICharts.ChartBlock(chart, $(this));
            chart.initChartBlock(block);
            blocks.push(block);

        });
        return blocks;
    }

    function initChartBlock(block) {
        var chart = this;
        block.notifyRangePositionEdited = function () {
            chart.updateZoomCommands();
            chart.delaySaveDisplayInterval(block);
        };
    }

    function resize() {
        $.each(this.chartBlocks, function (i, block) {
            block.resize();
        });
    }

    function loadData() {
        this.chartBlocks.forEach(function (chartBlock) {
            chartBlock.loadData();
        });
    }

    function loadFunctionData(chartFunc) {
        this.selectedItems = [];
        $("#showSelInfoButton").prop("disabled", true);
        var range = chartFunc.getTimelineRange();
        var dataChunk = this.dataLoader.getData(chartFunc.funcId, range);
        if (dataChunk != null)
            chartFunc.drawData(dataChunk);
        else
            chartFunc.draw(); // wenigstens aktuelle Daten neu zeichnen

        // Daten laden, wenn nicht im Pool oder nicht passend (Laenge bzw. Daten-Dichte)
        if (dataChunk == null || !dataChunk.isMatchingRange(range)) {
            this.dataLoader.loadData(chartFunc.funcId, range).then(function (chunk) {
                chartFunc.drawData(chunk);
            });
        }
    }

    function initCommands(chart) {
        $("#chart-button-times").click(function () {
            chart.editTimes($(this));
        });
        $("#chart-button-revert-zoom").click(function () {
            chart.revertZoom();
        });
        $("#chart-button-find").click(function () {
            chart.openSearch();
        });
        $("#chart-button-config").click(function () {
            chart.navToConfig();
        });
        $("#chart-button-add-block").click(function () {
            chart.addChartBlock();
        });
        $("#chart-button-remove-block").click(function () {
            chart.removeChartBlock(1);
        });
        $("#chart-button-sync").click(function () {
            // TODO: evtl. Menue fuer 2 Moeglichkeiten "Unten mit oben synchronisieren" und umgekehrt
            chart.syncChartBlock(1, 0);
        });
        $("#chart-button-anchor").click(function () {
            chart.toggleAnchorChartBlocks();
        });

        $("#showSelInfoButton").click(function () {
            chart.showSelInfo();
        }).prop("disabled", true);

        chart.chartBlocks.forEach(function (block) {
            chart.setupBlockCommands(block);
        });
        
        chart.updateZoomCommands();
        chart.updateBlockCommands();
    }

    function updateZoomCommands() {
        var canRevertZoom = false;
        for (var i = 0; i < this.chartBlocks.length; i++) {
            if (this.chartBlocks[i].canRevertZoom()) {
                canRevertZoom = true;
                break;
            }
        }
        $("#chart-button-revert-zoom").prop("disabled", !canRevertZoom);
    }

    function updateBlockCommands() {
        var multi = this.hasMultiChartBlocks();
        $("#chart-button-add-block").toggle(!multi); // add und remove-buttons alternativ
        $("#chart-button-remove-block").toggle(multi);
        $("#chart-button-anchor").prop("disabled", !multi).toggleClass("checked", multi && this.isAnchored);
        $("#chart-button-sync").prop("disabled", !multi);
        this.$view.find(".chart-block-close").toggle(multi);
    }

    function setupBlockCommands(block) {
        var chart = this;
        block.view.$view.find(".chart-block-close").click(function () {
            var $block = $(this).closest(".chart-block");
            var blockIdx = $block.hasClass("chart-block-multi-first") ? 0 : 1;
            chart.removeChartBlock(blockIdx);
        });
    }

    function hasMultiChartBlocks() {
        return this.chartBlocks.length > 1;
    }

    function addChartBlock() {
        if (this.hasMultiChartBlocks())
            return;

        var newBlock = this.chartBlocks[0].cloneChartBlock();
        this.initChartBlock(newBlock);
        this.chartBlocks.push(newBlock);

        this.chartBlocks.forEach(function (block, idx) {
            block.updateMultiClass(idx + 1);
        });

        this.setupBlockCommands(newBlock);
        newBlock.loadData(); // i.allg. aus dem Cache
        this.resize();
        this.updateBlockCommands();
    }

    function removeChartBlock(blockIdx) {
        if (blockIdx >= this.chartBlocks.length || this.chartBlocks.length == 1)
            return;

        if (this.isAnchored)
            this.setAnchorChartBlocks(false);

        var oldBlock = this.chartBlocks[blockIdx];
        oldBlock.view.$view.remove();
        oldBlock.notifyRangePositionEdited = null;
        this.chartBlocks.splice(blockIdx, 1);
        this.chartBlocks[0].updateMultiClass(0);

        this.resize();
        this.updateBlockCommands();
        this.updateZoomCommands(); // ggf. kein revert zoom mehr moeglich
    }

    function syncChartBlock(blockIdxSync, blockIdxSrc) {
        if (blockIdxSync >= this.chartBlocks.length || blockIdxSrc >= this.chartBlocks.length)
            return;

        var timelineSync = this.chartBlocks[blockIdxSync].timeline;
        var timelineSrc = this.chartBlocks[blockIdxSrc].timeline;
        timelineSync.setRangeAndPosition(timelineSrc.range.start, timelineSrc.range.stop, timelineSrc.position);

        this.chartBlocks.forEach(function (block) {
            block.captureAnchor(true);
        });
    }

    function toggleAnchorChartBlocks() {
        if (this.hasMultiChartBlocks()) {
            this.setAnchorChartBlocks(!this.isAnchored);
            this.updateBlockCommands();
        }
    }

    function setAnchorChartBlocks(set) {
        var chart = this;
        var notify = null;
        this.isAnchored = set;
        if (set) {
            notify = function (block) {
                chart.notifySyncAnchorChartBlock(block);
            };
        }

        this.chartBlocks.forEach(function (block) {
            block.captureAnchor(set);
            block.notifySyncAnchor = notify;
        });
    }

    function notifySyncAnchorChartBlock(srcBlock) {
        if (this.chartBlocks.length <= 1 || !this.isAnchored)
            return;

        var syncIdx = srcBlock == this.chartBlocks[0] ? 1 : 0;
        var syncBlock = this.chartBlocks[syncIdx];

        var diffStart = srcBlock.anchor.range.start.diffMilliseconds(srcBlock.timeline.range.start);
        var diffStop = srcBlock.anchor.range.stop.diffMilliseconds(srcBlock.timeline.range.stop);
        var diffPos = srcBlock.anchor.position.diffMilliseconds(srcBlock.timeline.position);

        var start = syncBlock.anchor.range.start.clone().addMilliseconds(diffStart);
        var stop = syncBlock.anchor.range.stop.clone().addMilliseconds(diffStop);
        var pos = syncBlock.anchor.position.clone().addMilliseconds(diffPos);

        syncBlock.timeline.setRangeAndPosition(start, stop, pos);
    }

    function revertZoom() {
        this.chartBlocks[0].revertZoom();

        // zweiten Diagrammblock nur, wenn nicht verankert; sonst wird synchronisiert
        if (this.chartBlocks.length > 1 && !this.isAnchored)
            this.chartBlocks[1].revertZoom();

        // updateZoomCommands bei Notification
    }

    // Zur Konfigurationsseite navigieren
    // Anzeigeparameter verpacken, damit hierhin zurueck navigiert werden kann
    function navToConfig() {
        var par = this.packChartParams();
        var href = basePath + "Charts/ChartConfig/Config?" + par;
        window.location = href;
    }

    // RouteValues fuer ChartParams als String
    // Bemerkung: $.param funktioniert nicht (liefert Blocks[0][Start]=... statt Blocks[0].Start=...), daher als String von Hand aufbauen
    function packChartParams() {
        var id = this.$view.data("chart-chartid");
        var par = "ChartId=" + id;
        this.chartBlocks.forEach(function (block, idx) {
            var blockPrefix = "&Blocks[" + idx + "].";
            par += blockPrefix + "Start=" + block.timeline.range.start.toJSON();
            par += blockPrefix + "Stop=" + block.timeline.range.stop.toJSON();
            par += blockPrefix + "Position=" + block.timeline.position.toJSON();
        });
        return par;
    }

    function editTimes($button) {
        var $dlg = $("#chartSelectTimesDlg");
        var dlg = new EPVICharts.Dialogs.SelectTimesDlg(this, $dlg);

        // Am Button positionieren
        var position;
        if ($button && $button.length) {
            position = { my: "left top", at: "left bottom", of: $button };
        }

        $dlg.openDialog({
            position: position,
            open: function () {
                dlg.open();
            }
        }, function () {
            return dlg.accept();
        });
    }

    function openSearch() {
        var $dlg = $("#chartFindDlg");
        if (!this.searchDlg || !$dlg.hasClass("ui-dialog-content")) {
            this.searchDlg = new EPVICharts.Dialogs.FindDlg(this, $dlg); // einmalig erstellen beim ersten Oeffnen
        }
        var dlg = this.searchDlg;
        $dlg.openDialog({
            create: function () {
                dlg.create();
            },
            open: function () {
                dlg.open();
            }
        }, function () {
            dlg.search();
        });
    }

    // Helper fuer Find-Dialog: sucht zu ChartFunctionId die ChartFunction
    function findFunc(funcId) {
        for (var areaIdx = 0; areaIdx < this.chartBlocks[0].chartAreas.length; areaIdx++) {
            var area = this.chartBlocks[0].chartAreas[areaIdx];
            for (var funcIdx = 0; funcIdx < area.chartFunctions.length; funcIdx++) {
                var func = area.chartFunctions[funcIdx];
                if (func.funcId == funcId) {
                    return func;
                }
            }
        }
        return null;
    }

    // Fuer interne Praesentationszwecke wird das letzte Anzeige-Intervall gespeichert (mit Verzoegerung)
    function delaySaveDisplayInterval(block) {
        if (this.saveDisplayIntervalTimeout) {
            window.clearTimeout(this.saveDisplayIntervalTimeout);
            this.saveDisplayIntervalTimeout = null;
        }

        if (!this.suppressSaveDisplayInterval) {
            this.saveDisplayIntervalTimeout = window.setTimeout(function () {
                this.saveDisplayIntervalTimeout = null;

                var range = block.timeline.range;
                var data = {
                    start: range.start.toJSON(),
                    stop: range.stop.toJSON()
                };
         
                $.post(basePath + "Charts/Chart/SaveDisplayInterval", data, function (result) {
                    if (result)
                        this.saveDisplayIntervalTimeout = true;
                });
            }, 3000);
        }
    }

    function showSelInfo() {
        if (this.selectedItems.length == 0)
            return;

        var columns = $.map(this.selectedItems, function (item) {
            var keyParts = item.id.split("#");
            var keyValue = null;
            if (keyParts.length == 2) {
                keyValue = { ProcessDataSpecId: keyParts[0], ProcessDataId: keyParts[1] };
            }
            return {
                KeyValue: keyValue,
                ColumnName: item.name
            };
        });
        var data = { viewModel: { Columns: columns } };
        data = $.toDictionary(data);
        $.get(basePath + "Charts/Chart/LoadInfo", data).done(function (response) {
            loadDialog(response);
            openInfoDialog();
        });
    }

    function loadDialog(dlg) {
        // erst leermachen, dann append (Bug - sonst verschwindet form-tag; http://forum.jquery.com/topic/is-jquery-stripping-form-tags)
        $("#dlgPlaceholder").html("").append(dlg);
    }

    function openInfoDialog() {
        var $dlg = $("#dlgPlaceholder .dialog");

        var buttonNames = $dlg.data("dlg-buttons");
        var buttonDef = {};
        buttonDef[buttonNames.OK] = function () {
            $(this).dialog("close");
        };

        var dlgSettings = {
            modal: true,
            buttons: buttonDef,
            open: function () {
                initInfoDialog($(this));
            },
            close: function (event, ui) {
                $(this).dialog("destroy");  // destroy raeumt auf, sonst sind spaeter mehrere unsichtbare Dialoge auf der Seite
            }
        };

        // Breite und Hoehe optional vorgeben: direkt als Style am Dialog-Element
        var width = $dlg[0].style.width;
        if ($dlg[0].style.width) {
            dlgSettings.width = $dlg.width(); // ohne 'px'
        }
        if ($dlg[0].style.height) {
            dlgSettings.height = $dlg.height(); // ohne 'px'
        }

        $dlg.dialog(dlgSettings);
    }

    function initInfoDialog($dlg) {
        $dlg.find(".grid").gridExtensions();
    }

    return Chart;
})();

$(function () {
    $(".charts").each(function () {
        new EPVICharts.Chart($(this));
    });
});
