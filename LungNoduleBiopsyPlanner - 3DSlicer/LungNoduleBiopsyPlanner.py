# Standard library imports
import logging
import os
import sys
import time
from pathlib import Path           
import subprocess

# Third-party imports
import numpy as np
import vtk
import qt
import ctk

# Slicer imports
import slicer
from slicer.ScriptedLoadableModule import *
from slicer.util import VTKObservationMixin

#
# LungNoduleBiopsyPlanner
#

class LungNoduleBiopsyPlanner(ScriptedLoadableModule):
    """Uses ScriptedLoadableModule base class, available at:
    https://github.com/Slicer/Slicer/blob/master/Base/Python/slicer/ScriptedLoadableModule.py
    """

    def __init__(self, parent):
            ScriptedLoadableModule.__init__(self, parent)
            self.parent.title = "LungNoduleBiopsyPlanner"
            self.parent.categories = ["AR Surgical Procedures"]
            self.parent.contributors = ["Chiara Pes, Alessio Scillia"]

            
    """
    This file was originally developed by Jean-Christophe Fillion-Robin, Kitware Inc., Andras Lasso, PerkLab,
    and Steve Pieper, Isomics, Inc. and was partially funded by NIH grant 3P41RR013218-12S1.
    """


#
# LungNoduleBiopsyPlannerWidget
#

class LungNoduleBiopsyPlannerWidget(ScriptedLoadableModuleWidget, VTKObservationMixin):
    """Uses ScriptedLoadableModuleWidget base class, available at:
    https://github.com/Slicer/Slicer/blob/master/Base/Python/slicer/ScriptedLoadableModule.py
    """

    def __init__(self, parent=None):
        """
        Called when the user opens the module the first time and the widget is initialized.
        """
        ScriptedLoadableModuleWidget.__init__(self, parent)
        VTKObservationMixin.__init__(self)  # needed for parameter node observation
        self.logic = None
        self._parameterNode = None
        self._updatingGUIFromParameterNode = False

    def setup(self):
        """
        Called when the user opens the module the first time and the widget is initialized.
        """
        ScriptedLoadableModuleWidget.setup(self)

        # Load widget from .ui file (created by Qt Designer).
        uiWidget = slicer.util.loadUI(self.resourcePath('UI/LungNoduleBiopsyPlanner.ui'))
        self.layout.addWidget(uiWidget)
        self.ui = slicer.util.childWidgetVariables(uiWidget)

        # Set scene in MRML widgets.
        uiWidget.setMRMLScene(slicer.mrmlScene)

        # Create logic class.
        self.logic = LungNoduleBiopsyPlannerLogic()

        # Connections
        self.addObserver(slicer.mrmlScene, slicer.mrmlScene.StartCloseEvent, self.onSceneStartClose)
        self.addObserver(slicer.mrmlScene, slicer.mrmlScene.EndCloseEvent, self.onSceneEndClose)
                    
        ### INPUTS SECTION
        self.ui.inputSelector.connect("currentNodeChanged(vtkMRMLNode*)", self.onInputVolumeUpdated)
        self.ui.inputVolumePath.connect("currentPathChanged(QString)", self.onInputVolumePathUpdated)
        self.ui.loadInputVolumeButton.connect("clicked(bool)", self.onLoadInputVolumeClicked)
        
        self.ui.imageWWSpinBox.connect("valueChanged(double)", self.onWWSpinBoxChanged)
        self.ui.imageHistogramSlideBar.connect("valuesChanged(double,double)", self.onImageHistogramSlideBarChanged)
        self.ui.imageWLSpinBox.connect("valueChanged(double)", self.onWLSpinBoxChanged)
        
        self.ui.TotalSegmentatorButton.connect('clicked(bool)', self.TotalSegmentatorButtonClicked)
        self.ui.NodulesButton.connect('clicked(bool)', self.NodulesButtonClicked)
        self.ui.AirwaysButton.connect('clicked(bool)', self.AirwaysButtonClicked)
        self.ui.SkinButton.connect('clicked(bool)', self.SkinButtonClicked)
        self.ui.ObstacleModelButton.clicked.connect(self.onGenerateObstacleModelClicked)
        self.ui.SegmentationShow3DButton_Skin.toggled.connect(self.onSkinShow3DToggled)
        self.ui.SegmentationShow3DButton_ObstacleModel.toggled.connect(self.onObstacleShow3DToggled)

        #### OpenIGTLink Connection SECTION
        self.ui.serverActiveCheckBox.connect("toggled(bool)", self.onActivateOpenIGTLinkConnectionClicked)

        # === Sezione Markups ===
        slicer.util.selectModule('Markups')
        self.markupsBox = ctk.ctkCollapsibleGroupBox()
        self.markupsBox.title = "Markups"
        self.markupsBox.collapsed = True
        self.layout.addWidget(self.markupsBox)
        
        self.markupsLayout = qt.QVBoxLayout(self.markupsBox)
        markupsWidget = slicer.modules.markups.widgetRepresentation()
        markupsWidget.setParent(None)
        self.markupsLayout.addWidget(markupsWidget)
        
        # === Sezione PercutaneousApproachAnalysis ===
        slicer.util.selectModule('PercutaneousApproachAnalysis')
        self.percutaneousBox = ctk.ctkCollapsibleGroupBox()
        self.percutaneousBox.title = "Percutaneous Approach Analysis"
        self.percutaneousBox.collapsed = True
        self.layout.addWidget(self.percutaneousBox)
        
        self.percutaneousLayout = qt.QVBoxLayout(self.percutaneousBox)
        percutaneousWidget = slicer.modules.percutaneousapproachanalysis.widgetRepresentation()
        percutaneousWidget.setParent(None)
        self.percutaneousLayout.addWidget(percutaneousWidget)

        # === Sezione PortPlacement ===
        slicer.util.selectModule('PortPlacement')
        self.portplaceBox = ctk.ctkCollapsibleGroupBox()
        self.portplaceBox.title = "PortPlacement"
        self.portplaceBox.collapsed = True
        self.layout.addWidget(self.portplaceBox)

        self.portplaceLayout = qt.QVBoxLayout(self.portplaceBox)
        portplaceWidget = slicer.modules.portplacement.widgetRepresentation()
        portplaceWidget.setParent(None)
        self.portplaceLayout.addWidget(portplaceWidget)

        # === Pulsante Export GLB ===
        self.exportGltfButton = qt.QPushButton("Export GLB")
        self.exportGltfButton.toolTip = "Export generated models and visible PortPlacement models to GLB"
        self.exportGltfButton.connect('clicked(bool)', self.onExportGltfClicked)
        self.layout.addWidget(self.exportGltfButton)

        # --- BLOCCO WEB SERVER ---
        self.webServerButton = qt.QPushButton("Start Web Server")
        self.webServerButton.toolTip = "Start/Stop local HTTP server to host the GLTF model for Unity"
        self.webServerButton.checkable = True
        self.webServerButton.connect('toggled(bool)', self.onWebServerToggled)
        self.layout.addWidget(self.webServerButton)
        
        self.lastExportDirectory = None
        self.ui.ObstacleModelButton.enabled = False

        # --- SPOSTAMENTO IN FONDO: OpenIGTLink & Volume Reslice Driver ---

        # 1. Spostiamo l'intero box OpenIGTLink originale in fondo al layout
        # (usare addWidget su un elemento esistente lo sposta automaticamente alla fine,
        # mantenendo intatti tutti i suoi eventi e le impostazioni del Qt Designer)
        if hasattr(self.ui, 'OpenIGTLinkCollapsibleButton'):
            self.layout.addWidget(self.ui.OpenIGTLinkCollapsibleButton)
            self.ui.OpenIGTLinkCollapsibleButton.collapsed = False

        # 2. Creiamo il box del Volume Reslice Driver subito sotto IGTLink
        self.resliceDriverBox = ctk.ctkCollapsibleGroupBox()
        self.resliceDriverBox.title = "Volume Reslice Driver (OpenIGTLink)"
        self.resliceDriverBox.collapsed = True
        self.resliceDriverLayout = qt.QVBoxLayout(self.resliceDriverBox)

        resliceDriverWidget = slicer.modules.volumereslicedriver.widgetRepresentation()
        resliceDriverWidget.setParent(None)
        self.resliceDriverLayout.addWidget(resliceDriverWidget)

        # Aggiungiamo il reslice driver DIRETTAMENTE in fondo al layout
        self.layout.addWidget(self.resliceDriverBox)

        # Make sure parameter node is initialized (needed for module reload)
        self.initializeParameterNode()
        self.initializeGUI()

    def onWebServerToggled(self, checked):
        if checked:
            directory = self.lastExportDirectory
            # Se non hai ancora esportato nulla, chiedi quale cartella servire
            if not directory:
                directory = qt.QFileDialog.getExistingDirectory(slicer.util.mainWindow(), "Select directory containing model.gltf")
                if not directory:
                    self.webServerButton.setChecked(False)
                    return
                self.lastExportDirectory = directory
            
            try:
                port = 8080
                self.logic.startWebServer(directory, port=port)
                self.addLog(f"Web server started on port {port} at: {directory}")
                self.webServerButton.text = "Stop Web Server (Port 8080)"
            except Exception as e:
                slicer.util.errorDisplay(f"Failed to start web server: {str(e)}")
                self.webServerButton.setChecked(False)
        else:
            self.logic.stopWebServer()
            self.addLog("Web server stopped.")
            self.webServerButton.text = "Start Web Server"


    def initializeGUI(self):
        """
            initailize the save directory using settings
        """
        settings = slicer.app.userSettings()
        # Save functionality managed by AR_Planner module
        # Uncomment if implementing save in this module:
        '''if settings.value(self.logic.SAVING_DIRECTORY): # if the settings exists
            self.ui.savingPath.directory = settings.value(self.logic.SAVING_DIRECTORY)'''
        if settings.value(self.logic.INPUT_VOLUME_PATH):
            self.ui.inputVolumePath.setCurrentPath(settings.value(self.logic.INPUT_VOLUME_PATH))
        '''if settings.value(self.logic.MODELS_DIRECTORY):
            self.ui.modelsPath.setCurrentPath(settings.value(self.logic.MODELS_DIRECTORY))'''
        '''if settings.value(self.logic.SCREWS_DIRECTORY):
            self.ui.screwDirButton.directory = settings.value(self.logic.SCREWS_DIRECTORY)'''


    def cleanup(self):
        """
        Called when the application closes and the module widget is destroyed.
        """
        if self.logic:
            self.logic.stopWebServer()
        self.removeObservers()

    def enter(self):
        """
        Called each time the user opens this module.
        """
        # Make sure parameter node exists and observed
        self.initializeParameterNode()

    def onSceneStartClose(self, caller, event):
        """
        Called just before the scene is closed.
        """
        # Parameter node will be reset, do not use it anymore
        self.setParameterNode(None)

    def onSceneEndClose(self, caller, event):
        """
        Called just after the scene is closed.
        """
        # If this module is shown while the scene is closed then recreate a new parameter node immediately
        if self.parent.isEntered:
                self.initializeParameterNode()

    #
    # UI functions
    #

    def onInputVolumePathUpdated(self, path):
        '''
        Updates the input volume path in the settings
        '''
        settings = slicer.app.userSettings()
        settings.setValue(self.logic.INPUT_VOLUME_PATH, path)
        # enable the button
        self.ui.loadInputVolumeButton.enabled = True

    def onLoadInputVolumeClicked(self):
        path = self.ui.inputVolumePath.currentPath
        filename = os.path.splitext(os.path.basename(path))[0]
        
        existingVolume = slicer.util.getFirstNodeByName(filename)
        if existingVolume:
            self.ui.inputSelector.setCurrentNode(existingVolume)
            self.addLog(f"Volume {filename} già caricato, riutilizzo nodo esistente.")
        else:
            volumeNode = slicer.util.loadVolume(path)
            self.ui.inputSelector.setCurrentNode(volumeNode)
            self.addLog(f"Volume {filename} caricato. Il ricentramento avverrà prima della connessione OpenIGTLink.")
        
        self.ui.loadInputVolumeButton.enabled = False

    def onInputVolumeUpdated(self):
        """
        This method is called when the input volume is changed.
        It updates the range of the histogram slide bar, and the WW and WL spin boxes.
        """
        self.updateParameterNodeFromGUI()
        self.logic.updateHistLimitsFromInput()
        # Update the slide bar limits
        self.ui.imageHistogramSlideBar.minimum = float(self._parameterNode.GetParameter(self.logic.IMAGE_HIST_SLIDEBAR_minLimit))
        self.ui.imageHistogramSlideBar.maximum = float(self._parameterNode.GetParameter(self.logic.IMAGE_HIST_SLIDEBAR_maxLimit))
        # Set the maximum and minimum values for the WW and WL spin boxes
        maxWidth = float(self._parameterNode.GetParameter(self.logic.IMAGE_HIST_SLIDEBAR_maxLimit)) - float(self._parameterNode.GetParameter(self.logic.IMAGE_HIST_SLIDEBAR_minLimit))
        self.ui.imageWWSpinBox.minimum = 0.0
        self.ui.imageWWSpinBox.maximum = maxWidth
        minLevel = float(self._parameterNode.GetParameter(self.logic.IMAGE_HIST_SLIDEBAR_minLimit))
        maxLevel = float(self._parameterNode.GetParameter(self.logic.IMAGE_HIST_SLIDEBAR_maxLimit))
        self.ui.imageWLSpinBox.maximum = maxLevel
        self.ui.imageWLSpinBox.minimum = minLevel
        # update the window level to be zero and the width to be half the maximum in the parameter node
        self._parameterNode.SetParameter(self.logic.WINDOW_LEVEL, str(0))
        self._parameterNode.SetParameter(self.logic.WINDOW_WIDTH, str(0.5*maxWidth))

    def onWWSpinBoxChanged(self, value):
        """
        Updates the Window Width value in the parameter node
        """
        parameterNode = self.logic.getParameterNode()
        parameterNode.SetParameter(self.logic.WINDOW_WIDTH, str(value))
        self.updateGUIFromParameterNode()
        self.logic.UpdateImageValuesWithSlider()

    def onWLSpinBoxChanged(self, value):
        """
        Updates the Window Level value in the parameter node
        """
        parameterNode = self.logic.getParameterNode()
        parameterNode.SetParameter(self.logic.WINDOW_LEVEL, str(value))
        self.updateGUIFromParameterNode()
        self.logic.UpdateImageValuesWithSlider()

    def onImageHistogramSlideBarChanged(self):
        """
        Updates the Window Width and Window Level values in the parameter node
        """
        parameterNode = self.logic.getParameterNode()
        parameterNode.SetParameter(self.logic.WINDOW_WIDTH, str(self.ui.imageHistogramSlideBar.maximumValue - self.ui.imageHistogramSlideBar.minimumValue))
        parameterNode.SetParameter(self.logic.WINDOW_LEVEL, str((self.ui.imageHistogramSlideBar.maximumValue + self.ui.imageHistogramSlideBar.minimumValue)/2))
        self.updateGUIFromParameterNode()
        self.logic.UpdateImageValuesWithSlider()

    def populateSegmentationsList(self):
        """
        Populates the list of segmentations in the GUI.
        """
        self.ui.obstacleListWidget.clear()
        segmentationNodes = slicer.util.getNodesByClass("vtkMRMLSegmentationNode")
        
        for segNode in segmentationNodes:
            segmentation = segNode.GetSegmentation()
            segmentIDs = vtk.vtkStringArray()
            segmentation.GetSegmentIDs(segmentIDs)
            
            for i in range(segmentIDs.GetNumberOfValues()):
                segmentID = segmentIDs.GetValue(i)
                segmentName = segmentation.GetSegment(segmentID).GetName()
                
                # Testo visibile nella lista
                itemText = f"{segmentName} ({segNode.GetName()})"
                item = qt.QListWidgetItem(itemText)

                # Salviamo nodo + segmentID nei dati
                item.setData(qt.Qt.UserRole, (segNode.GetID(), segmentID))
                self.ui.obstacleListWidget.addItem(item)


    def TotalSegmentatorButtonClicked(self):
        """
        Run TotalSegmentator segmentation when user clicks "TotalSegmentator" button.
        """
        self.updateParameterNodeFromGUI()
         # Controlla se esiste già una segmentazione chiamata "TotalSegmentator_SegmentationTotal"
        '''existingSegmentation = slicer.util.getNode(pattern="TotalSegmentator_SegmentationTotal*")
        if existingSegmentation:
            self.addLog("Segmentazione già presente: 'TotalSegmentator_SegmentationTotal'")
            self.populateSegmentationsList()
            self.ui.ObstacleModelButton.enabled = True
            self.ui.TotalSegmentatorButton.enabled = False
            self.ui.SegmentationShow3DButton_Total.setSegmentationNode(existingSegmentation)
            return'''
        
        try:
            try:
                import TotalSegmentator
                Logic = TotalSegmentator.TotalSegmentatorLogic()
                Logic.logCallback = self.addLog
            except ImportError:
                slicer.util.errorDisplay("TotalSegmentator non trovato. Assicurati di aver installato l'estensione tramite l'Extension Manager di Slicer.")
                return

            # Get input volume from selector (avoid reloading and creating duplicates)
            inputVolume = self.ui.inputSelector.currentNode()
            if not inputVolume:
                raise ValueError("No input volume selected")

            # Create segmentation node
            segmentationNode = slicer.mrmlScene.AddNewNodeByClass("vtkMRMLSegmentationNode")
            segmentationNode.SetName("TotalSegmentator_SegmentationTotal")
            segmentationNode.CreateDefaultDisplayNodes()  # Create display node immediately
            
            with slicer.util.tryWithErrorDisplay("Failed to compute segmentation.", waitCursor=True):
                # Run the segmentation
                Logic.process(inputVolume, segmentationNode, 
                            quality="normal",  # Full resolution for better accuracy 
                            cpu=False,   # Use GPU if available
                            task="total",
                            interactive=True)
                
                # Update display
                slicer.util.setSliceViewerLayers(background=inputVolume)
                segmentationNode.GetDisplayNode().SetVisibility(True)
                
                # Collega il nodo di segmentazione al bottone Show3D
                self.ui.SegmentationShow3DButton_Total.setSegmentationNode(segmentationNode)                
                self.addLog("Total segmentation completed successfully")
                
        except Exception as e:
            slicer.util.errorDisplay(f"Failed to run TotalSegmentator: {str(e)}")
            import traceback
            traceback.print_exc()
            return

        # Disable button after successful completion
        self.ui.TotalSegmentatorButton.enabled = True
        self.populateSegmentationsList()
        self.ui.ObstacleModelButton.enabled = True

  
    def NodulesButtonClicked(self):
        """
        Run TotalSegmentator segmentation when user clicks "Nodules" button.
        """
        self.updateParameterNodeFromGUI()
        
        try:
            try:
                import TotalSegmentator
                Logic = TotalSegmentator.TotalSegmentatorLogic()
                Logic.logCallback = self.addLog
            except ImportError:
                slicer.util.errorDisplay("TotalSegmentator non trovato. Assicurati di aver installato l'estensione tramite l'Extension Manager di Slicer.")
                return
            
            # Get input volume
            inputVolume = self.ui.inputSelector.currentNode()
            if not inputVolume:
                raise ValueError("No input volume selected")

            # Create segmentation node
            segmentationNode = slicer.mrmlScene.AddNewNodeByClass("vtkMRMLSegmentationNode")
            segmentationNode.SetName("TotalSegmentator_SegmentationNodules")
            segmentationNode.CreateDefaultDisplayNodes()  # Create display node immediately
            
            with slicer.util.tryWithErrorDisplay("Failed to compute segmentation.", waitCursor=True):
                # Run the segmentation
                Logic.process(inputVolume, segmentationNode, 
                            quality="normal",  # Full resolution for better accuracy 
                            cpu=False,   # Use GPU if available
                            task="lung_nodules",
                            interactive=True)
                
                # --- INIZIO BLOCCO FILTRO E COLORAZIONE NODULI ---
                segmentation = segmentationNode.GetSegmentation()
                segmentIDs = vtk.vtkStringArray()
                segmentation.GetSegmentIDs(segmentIDs)
                
                displayNode = segmentationNode.GetDisplayNode()
                
                for i in range(segmentIDs.GetNumberOfValues()):
                    segmentID = segmentIDs.GetValue(i)
                    segment = segmentation.GetSegment(segmentID)
                    name = segment.GetName().lower()
                    
                    if "nodule" in name:
                        # Diamo al nodulo un colore verde puro (coerente con Unity)
                        segment.SetColor(0.0, 1.0, 0.0)
                        # Assicuriamoci che sia visibile in 3D
                        displayNode.SetSegmentVisibility3D(segmentID, True)
                    elif "lung" in name:
                        # Nascondiamo i polmoni dalla vista 3D (e dall'export verso Unity)
                        displayNode.SetSegmentVisibility3D(segmentID, False)
                        # Nascondiamo i polmoni anche dalle viste 2D (Red, Yellow, Green)
                        displayNode.SetSegmentVisibility(segmentID, False)
                # --- FINE BLOCCO FILTRO ---

                # Update display
                slicer.util.setSliceViewerLayers(background=inputVolume)
                segmentationNode.GetDisplayNode().SetVisibility(True)
                
                # Collega il nodo di segmentazione al bottone Show3D
                self.ui.SegmentationShow3DButton_Nodules.setSegmentationNode(segmentationNode)
                
                self.addLog("Lung nodules segmentation completed successfully")
                
        except Exception as e:
            slicer.util.errorDisplay(f"Failed to run TotalSegmentator: {str(e)}")
            import traceback
            traceback.print_exc()
            return

        # Disable button after successful completion
        self.ui.NodulesButton.enabled = True


    def AirwaysButtonClicked(self):
        """
        Run TotalSegmentator segmentation when user clicks "TotalSegmentator" button.
        """
        self.updateParameterNodeFromGUI()
        
        try:
            try:
                import TotalSegmentator
                Logic = TotalSegmentator.TotalSegmentatorLogic()
                Logic.logCallback = self.addLog
            except ImportError:
                slicer.util.errorDisplay("TotalSegmentator non trovato. Assicurati di aver installato l'estensione tramite l'Extension Manager di Slicer.")
                return
            
            
            # Get input volume from selector (avoid reloading and creating duplicates)
            inputVolume = self.ui.inputSelector.currentNode()
            if not inputVolume:
                raise ValueError("No input volume selected")

            # Create segmentation node
            segmentationNode = slicer.mrmlScene.AddNewNodeByClass("vtkMRMLSegmentationNode")
            segmentationNode.SetName("TotalSegmentator_SegmentationAirways")
            segmentationNode.CreateDefaultDisplayNodes()  # Create display node immediately
            
            with slicer.util.tryWithErrorDisplay("Failed to compute segmentation.", waitCursor=True):
                # Run the segmentation
                Logic.process(inputVolume, segmentationNode, 
                            quality="normal",  # Full resolution for better accuracy 
                            cpu=False,   # Use GPU if available
                            task="lung_vessels",
                            interactive=True)
                
                 # Update display
                slicer.util.setSliceViewerLayers(background=inputVolume)
                segmentationNode.GetDisplayNode().SetVisibility(True)
                
                # Collega il nodo di segmentazione al bottone Show3D
                self.ui.SegmentationShow3DButton_Airways.setSegmentationNode(segmentationNode)
                
                self.addLog("Airways and vessels segmentation completed successfully")
                
        except Exception as e:
            slicer.util.errorDisplay(f"Failed to run TotalSegmentator: {str(e)}")
            import traceback
            traceback.print_exc()
            return

        # Disable button after successful completion
        self.ui.AirwaysButton.enabled = True
        self.populateSegmentationsList()
        self.ui.ObstacleModelButton.enabled = True

    def SkinButtonClicked(self):
        """
        Run TotalSegmentator segmentation for body and create hollow skin surface.
        """
        self.updateParameterNodeFromGUI()

        try:
            try:
                import TotalSegmentator
                Logic = TotalSegmentator.TotalSegmentatorLogic()
                Logic.logCallback = self.addLog
            except ImportError:
                slicer.util.errorDisplay("TotalSegmentator non trovato. Assicurati di aver installato l'estensione tramite l'Extension Manager di Slicer.")
                return
            

            # Get input volume from selector (avoid reloading and creating duplicates)
            inputVolume = self.ui.inputSelector.currentNode()
            if not inputVolume:
                raise ValueError("No input volume selected")

            # Create segmentation node for TotalSegmentator result
            segmentationNode = slicer.mrmlScene.AddNewNodeByClass("vtkMRMLSegmentationNode", "TotalSegmentator_Body")
            segmentationNode.CreateDefaultDisplayNodes()  # Create display node immediately

            with slicer.util.tryWithErrorDisplay("Failed to compute segmentation.", waitCursor=True):
                Logic.process(
                    inputVolume,
                    segmentationNode,
                    quality="normal",
                    cpu=False,
                    task="body",
                    interactive=True
                )

                # Check for 'body_trunc' segment
                sourceSegmentID = "body_trunc"
                sourceSegment = segmentationNode.GetSegmentation().GetSegment(sourceSegmentID)
                if sourceSegment is None:
                    raise ValueError(f"Segment '{sourceSegmentID}' not found in the segmentation")

                # Create new segmentation node for skin model
                skinModelSegmentation = slicer.mrmlScene.AddNewNodeByClass("vtkMRMLSegmentationNode", "skin_model")
                skinModelSegmentation.CreateDefaultDisplayNodes()

                # Copy segment
                skinModelSegmentation.GetSegmentation().CopySegmentFromSegmentation(
                    segmentationNode.GetSegmentation(), sourceSegmentID)
                
                skinSegment = skinModelSegmentation.GetSegmentation().GetSegment(sourceSegmentID)
                skinSegment.SetName("skin")
                
                # IMPOSTA IL COLORE QUI (es. Color carne: R=0.9, G=0.75, B=0.65)
                skinSegment.SetColor(0.9, 0.75, 0.65)
                

                # 1. Crea il Segment Editor Node
                segmentEditorNode = slicer.mrmlScene.AddNewNodeByClass("vtkMRMLSegmentEditorNode")

                # 2. Crea il widget se non esiste
                segmentEditorWidget = slicer.qMRMLSegmentEditorWidget()
                segmentEditorWidget.setMRMLScene(slicer.mrmlScene)

                # 3. Associa il nodo dei parametri
                segmentEditorWidget.setMRMLSegmentEditorNode(segmentEditorNode)

                # 4. Associa la segmentazione
                segmentEditorWidget.setSegmentationNode(skinModelSegmentation)

                # 5. Associa il volume di origine
                segmentEditorWidget.setSourceVolumeNode(inputVolume)

                # 6. Imposta effetto e parametri
                segmentEditorWidget.setActiveEffectByName("Hollow")
                effect = segmentEditorWidget.activeEffect()
                if not effect:
                    raise RuntimeError("Failed to activate 'Hollow' effect")

                effect.setParameter("ShellThickness", "3.0")

                # 7. Applica effetto
                effect.self().onApply()

                # Rename result
                for segmentID in skinModelSegmentation.GetSegmentation().GetSegmentIDs():
                    if segmentID != "skin":
                        skinModelSegmentation.GetSegmentation().GetSegment(segmentID).SetName("Skin_model")

                # Clean up segment editor
                segmentEditorWidget = None

                # Export segments to models
                shNode = slicer.mrmlScene.GetSubjectHierarchyNode()
                exportFolderItemId = shNode.CreateFolderItem(shNode.GetSceneItemID(), "Segments")
                slicer.modules.segmentations.logic().ExportAllSegmentsToModels(skinModelSegmentation, exportFolderItemId)

                # Hide the segmentation node: only the exported model node should be visible in 3D
                skinModelSegmentation.GetDisplayNode().SetVisibility(False)

                # Keep model nodes hidden initially; user enables via Show 3D button
                for modelNode in slicer.util.getNodesByClass('vtkMRMLModelNode'):
                    if modelNode.GetName().startswith("Skin"):
                        self.addLog("Smoothing modello Skin in corso...")

                        # 1. Smoothing iniziale per ridurre l'effetto voxel/slice
                        self.logic.smoothModel(
                            modelNode,
                            modelNode,
                            iterations=40,
                            passBand=0.06
                        )

                        # 2. Decimazione meno aggressiva
                        self.addLog("Decimazione modello Skin in corso (riduzione 50%)...")
                        self.logic.decimate(
                            modelNode,
                            modelNode,
                            reductionFactor=0.50,
                            decimateBoundary=True
                        )

                        # 3. Smoothing leggero finale + ricalcolo normals
                        self.logic.smoothModel(
                            modelNode,
                            modelNode,
                            iterations=15,
                            passBand=0.10
                        )

                        modelNode.GetDisplayNode().SetVisibility(False)

                # Enable the Show 3D button now that the model exists
                self.ui.SegmentationShow3DButton_Skin.enabled = True


                self.addLog("Skin segmentation and hollow surface creation completed successfully")

        except Exception as e:
            slicer.util.errorDisplay(f"Failed to run segmentation and hollow surface creation: {str(e)}")
            import traceback
            traceback.print_exc()
            return

        # Disable the button to avoid rerunning
        self.ui.SkinButton.enabled = True


    def onSkinShow3DToggled(self, checked):
        for modelNode in slicer.util.getNodesByClass('vtkMRMLModelNode'):
            if modelNode.GetName().startswith("Skin") and modelNode.GetDisplayNode():
                modelNode.GetDisplayNode().SetVisibility(checked)

    def _isNodeUnderFolder(self, node, folderName):
        shNode = slicer.mrmlScene.GetSubjectHierarchyNode()
        itemId = shNode.GetItemByDataNode(node)
        if itemId <= 0:
            return False
        parentItemId = shNode.GetItemParent(itemId)
        if parentItemId <= 0:
            return False
        return shNode.GetItemName(parentItemId) == folderName

    def _hasAncestorFolderContaining(self, node, folderNamePart):
        shNode = slicer.mrmlScene.GetSubjectHierarchyNode()
        itemId = shNode.GetItemByDataNode(node)
        if itemId <= 0:
            return False

        currentItemId = shNode.GetItemParent(itemId)
        folderNamePart = folderNamePart.lower()
        while currentItemId > 0:
            if folderNamePart in shNode.GetItemName(currentItemId).lower():
                return True
            currentItemId = shNode.GetItemParent(currentItemId)

        return False

    def onObstacleShow3DToggled(self, checked):
        for modelNode in slicer.util.getNodesByClass('vtkMRMLModelNode'):
            modelName = modelNode.GetName()
            displayNode = modelNode.GetDisplayNode()
            if not displayNode:
                continue

            # Show/hide only the exported obstacle surface model in ObstacleModels folder.
            if modelName == "ObstacleModel" and self._isNodeUnderFolder(modelNode, "ObstacleModels"):
                displayNode.SetVisibility(checked)
                continue

            # Keep auxiliary or duplicate obstacle nodes hidden.
            if modelName.startswith("ObstacleModel"):
                displayNode.SetVisibility(False)

    def _isRequestedAnatomySegment(self, segmentName):
        # Convertiamo tutto in minuscolo e sostituiamo gli underscore con gli spazi 
        # così copriamo sia "rib_1" che "rib 1"
        name = segmentName.lower().replace("_", " ")

        if "skin" in name or name in {"body", "body trunc"}: return True
        if "nodule" in name: return True
        if any(token in name for token in ["trachea", "bronch", "airway", "vessel", "blood vessel"]): return True
        # Segmenti del task lung_vessels
        if any(token in name for token in ["lung arteries", "lung veins", "lung airways", "lung airways wall"]): return True
        if "lung" in name and not any(token in name for token in ["nodule", "vessel", "airway", "trachea", "bronch", "artery", "arteries", "vein", "veins"]): return True

        # CONTROLLO OSSA CORRETTO
        bone_keywords = [
                "rib", "vertebra", "sternum", "clavicula", "scapula", "sacrum",
                "humerus", "femur", "hip", "fibula", "tibia", "ulna", "radius", 
                "skull", "patella", "bone"
            ]
        if any(keyword in name for keyword in bone_keywords):
            return True

        return False
    
    def _collectGltfExportModelNodes(self):
        exportNodes = []
        for modelNode in slicer.util.getNodesByClass('vtkMRMLModelNode'):
            modelName = modelNode.GetName()
            displayNode = modelNode.GetDisplayNode()
            if not displayNode:
                continue

            lowerName = modelName.lower()

            # Never export obstacle models.
            if lowerName.startswith("obstaclemodel"):
                continue

            includeNode = False

            # Include skin model generated by this module.
            if lowerName.startswith("skin"):
                includeNode = True

            # Include Tool model from PortPlacement if present.
            if "tool" in lowerName or self._hasAncestorFolderContaining(modelNode, "portplacement"):
                includeNode = True

            if includeNode:
                exportNodes.append(modelNode)

        return exportNodes

    def _collectGltfExportSegments(self):
        """
        Raccoglie TUTTI i segmenti di tutte le segmentazioni
        insieme al relativo colore del segmento.
        """
        exportSegments = []
        for segNode in slicer.util.getNodesByClass('vtkMRMLSegmentationNode'):
            segNodeNameLower = (segNode.GetName() or "").lower()

            # Never export obstacle segmentation content.
            if "obstacle" in segNodeNameLower:
                continue
            if "totalsegmentator_body" in segNodeNameLower:
                continue
            if "skin" in segNodeNameLower:
                continue

            segmentation = segNode.GetSegmentation()
            if not segmentation:
                continue

            segmentIds = vtk.vtkStringArray()
            segmentation.GetSegmentIDs(segmentIds)

            for i in range(segmentIds.GetNumberOfValues()):
                segmentId = segmentIds.GetValue(i)
                segment = segmentation.GetSegment(segmentId)
                if not segment:
                    continue

                segmentName = segment.GetName() or ""
                segmentNameLower = segmentName.lower()

                # Extra safety: skip obstacle-like segments even if node name is generic.
                if "obstaclemodel" in segmentNameLower or "obstacle model" in segmentNameLower:
                    continue

                # Colore segmento in RGB [0..1]
                try:
                    segmentColor = segment.GetColor()  # spesso tuple/list
                except Exception:
                    rgb = [1.0, 1.0, 1.0]
                    try:
                        segment.GetColor(rgb)
                    except Exception:
                        pass
                    segmentColor = rgb

                exportSegments.append((segNode, segmentId, segmentName, segmentColor))

        return exportSegments

    def exportModelsToGltf(self, outputFilePath):
        exportModelNodes = self._collectGltfExportModelNodes()
        exportSegments = self._collectGltfExportSegments()

        self.addLog(f"DEBUG: Trovati {len(exportModelNodes)} Model Node e {len(exportSegments)} Segmenti da esportare.")

        if len(exportModelNodes) == 0 and len(exportSegments) == 0:
            raise RuntimeError("No requested anatomy/tool nodes available to export")

        try:
            import trimesh
        except ImportError:
            self.addLog("Installazione di trimesh in corso. Attendere...")
            slicer.util.pip_install("trimesh")
            import trimesh

        import numpy as np
        import vtk.util.numpy_support as vtk_np

        scene = trimesh.Scene()
        shNode = slicer.mrmlScene.GetSubjectHierarchyNode()
        exportFolderId = None

        meshes_by_category = {}

        # Mantieni qui la tua logica di macro-categorie.
        # Puoi renderla più sofisticata quando vuoi.
        def get_clean_category(raw_name):
            lower_name = (raw_name or "").lower().replace("_", " ")

            if "tool" in lower_name:
                return "Tool"

            if "skin" in lower_name or "body" in lower_name:
                return "skin"

            if "airway" in lower_name or "trachea" in lower_name or "bronch" in lower_name:
                return "Airways"

            # -------------------------------
            # OSSA SEPARATE IN SOTTOGRUPPI
            # -------------------------------

            # Coste
            if (
                "rib" in lower_name
                or "costal" in lower_name
            ):
                return "Ribs"

            # Colonna vertebrale
            if (
                "vertebra" in lower_name
                or "spine" in lower_name
                or "sacrum" in lower_name
            ):
                return "Spine"

            # Sterno
            if "sternum" in lower_name:
                return "Sternum"

            # Clavicole e scapole
            if (
                "clavicula" in lower_name
                or "clavicle" in lower_name
                or "scapula" in lower_name
            ):
                return "ClaviclesScapulae"

            # Altre ossa eventuali
            other_bone_keywords = [
                "humerus", "femur", "hip", "fibula", "tibia",
                "ulna", "radius", "skull", "patella", "bone"
            ]

            if any(b in lower_name for b in other_bone_keywords):
                return "BonesOther"

            # -------------------------------
            # VASI
            # -------------------------------

            vein_keywords = [
                "lung veins",
                "pulmonary venous system",
                "pulmonary vein",
                "pulmonary veins"
            ]

            if any(k in lower_name for k in vein_keywords):
                return "PulmonaryVeins"

            artery_keywords = [
                "lung arteries",
                "pulmonary artery",
                "pulmonary arteries"
            ]

            if any(k in lower_name for k in artery_keywords):
                return "PulmonaryArteries"

            if "nodule" in lower_name:
                return "nodule"

            if "lung" in lower_name:
                return "Lung"

            return lower_name.replace(" ", "_")

        def _children_set(folderItemId):
            ids = set()
            children = vtk.vtkIdList()
            shNode.GetItemChildren(folderItemId, children)
            for i in range(children.GetNumberOfIds()):
                ids.add(children.GetId(i))
            return ids

        def _to_rgba255(rgb01):
            r = int(np.clip(float(rgb01[0]), 0.0, 1.0) * 255)
            g = int(np.clip(float(rgb01[1]), 0.0, 1.0) * 255)
            b = int(np.clip(float(rgb01[2]), 0.0, 1.0) * 255)
            return np.array([r, g, b, 255], dtype=np.uint8)

        def process_node(node, raw_name, rgb_color=None):
            poly = node.GetPolyData()
            if not poly or poly.GetNumberOfPoints() == 0:
                print(f"DEBUG SKIP: '{raw_name}' non ha geometria (0 punti).")
                return

            transformNode = node.GetParentTransformNode()
            if transformNode:
                transform = vtk.vtkGeneralTransform()
                transformNode.GetTransformToWorld(transform)
                t_filter = vtk.vtkTransformPolyDataFilter()
                t_filter.SetInputData(poly)
                t_filter.SetTransform(transform)
                t_filter.Update()
                poly = t_filter.GetOutput()

            triFilter = vtk.vtkTriangleFilter()
            triFilter.SetInputData(poly)
            triFilter.Update()
            triPoly = triFilter.GetOutput()

            if triPoly.GetNumberOfPoints() == 0:
                return

            verts = vtk_np.vtk_to_numpy(triPoly.GetPoints().GetData())
            polys_np = vtk_np.vtk_to_numpy(triPoly.GetPolys().GetData())

            if len(polys_np) > 0:
                faces = polys_np.reshape(-1, 4)[:, 1:4]
            else:
                faces = np.empty((0, 3), dtype=np.int64)

            mesh = trimesh.Trimesh(vertices=verts, faces=faces, process=False)

            # Se disponibile, applica colore importato da segmento/modello
            if rgb_color is not None:
                rgba = _to_rgba255(rgb_color)
                if len(faces) > 0:
                    face_colors = np.tile(rgba, (len(faces), 1))
                    mesh.visual = trimesh.visual.ColorVisuals(mesh=mesh, face_colors=face_colors)
                else:
                    vertex_colors = np.tile(rgba, (len(verts), 1))
                    mesh.visual = trimesh.visual.ColorVisuals(mesh=mesh, vertex_colors=vertex_colors)

            category = get_clean_category(raw_name)

            print(f"DEBUG MESH PROCESSATA: Nome originale '{raw_name}' -> Gruppo '{category}' (Vertici: {len(verts)})")

            if category not in meshes_by_category:
                meshes_by_category[category] = []
            meshes_by_category[category].append(mesh)

        try:
            # 1) Model nodes già presenti (tool/skin, ecc.)
            for modelNode in exportModelNodes:
                modelColor = None
                d = modelNode.GetDisplayNode()
                if d:
                    try:
                        modelColor = d.GetColor()
                    except Exception:
                        modelColor = None
                process_node(modelNode, modelNode.GetName(), modelColor)

            # 2) Segmenti: export uno-per-uno per mantenere mapping preciso nome->colore
            if len(exportSegments) > 0:
                for segNode, segmentId, segmentName, segmentColor in exportSegments:
                    
                    tempFolderId = shNode.CreateFolderItem(shNode.GetSceneItemID(), f"TempGltf_{segmentId}")

                    strArray = vtk.vtkStringArray()
                    strArray.InsertNextValue(segmentId)
                    
                    # Esporta il singolo segmento nella sua cartella esclusiva
                    slicer.modules.segmentations.logic().ExportSegmentsToModels(segNode, strArray, tempFolderId)

                    # Prendi i figli diretti appena creati
                    children = vtk.vtkIdList()
                    shNode.GetItemChildren(tempFolderId, children)

                    for i in range(children.GetNumberOfIds()):
                        itemId = children.GetId(i)
                        tempModel = shNode.GetItemDataNode(itemId)
                        if tempModel and tempModel.IsA("vtkMRMLModelNode"):
                            # --- DECIMAZIONE ---
                            category = get_clean_category(segmentName)

                            if category in ["Ribs", "Spine", "Sternum", "ClaviclesScapulae"]:
                                reduction = 0.60
                            elif category in ["PulmonaryArteries", "PulmonaryVeins", "Airways"]:
                                reduction = 0.55
                            elif category == "Lung":
                                reduction = 0.80
                            else:
                                reduction = 0.75

                            self.addLog(
                                f"Decimazione di {segmentName} → gruppo {category}, reduction={reduction}"
                            )

                            self.logic.decimate(
                                tempModel,
                                tempModel,
                                reductionFactor=reduction,
                                decimateBoundary=True
                            )

                            process_node(tempModel, segmentName, segmentColor)
                            
                            # --- MODIFICA QUI ---
                            # FONDAMENTALE: Rimuove fisicamente il modello dalla scena 
                            # così non compare improvvisamente sullo schermo!
                            slicer.mrmlScene.RemoveNode(tempModel)
                            # --------------------

                    # Distruggi la cartella temporanea
                    shNode.RemoveItem(tempFolderId)

            print("--- RESOCONTO UNIONE MESH ---")
            for category, mesh_list in meshes_by_category.items():
                print(f"DEBUG GRUPPO: '{category}' contiene {len(mesh_list)} frammenti da unire.")
                if len(mesh_list) == 1:
                    final_mesh = mesh_list[0]
                elif len(mesh_list) > 1:
                    final_mesh = trimesh.util.concatenate(mesh_list)
                else:
                    continue

                scene.add_geometry(final_mesh, node_name=category)

            scene.export(outputFilePath)
            self.addLog(f"Export GLTF completato con successo in: {outputFilePath}")

        finally:
            if exportFolderId is not None:
                shNode.RemoveItem(exportFolderId)

    def onExportGltfClicked(self):
        inputPath = self.ui.inputVolumePath.currentPath
        defaultDir = Path(inputPath).parent if inputPath else Path.home()
        # Cambiato da .gltf a .glb
        defaultFile = defaultDir / "model.glb" 

        selectedFile = qt.QFileDialog.getSaveFileName(
            slicer.util.mainWindow(),
            "Export models to glTF Binary",
            str(defaultFile),
            "glTF Binary files (*.glb)" # Cambiato qui
        )

        if isinstance(selectedFile, tuple):
            selectedFile = selectedFile[0]

        if not selectedFile:
            return

        # Cambiato controllo estensione
        if not selectedFile.lower().endswith('.glb'):
            selectedFile += '.glb'

        try:
            self.exportModelsToGltf(selectedFile)
            self.addLog(f"GLB exported: {selectedFile}")
            
            # Avvia/Aggiorna il server automaticamente
            self.lastExportDirectory = str(Path(selectedFile).parent)
            
            if not self.webServerButton.isChecked():
                self.webServerButton.setChecked(True)
            else:
                self.logic.startWebServer(self.lastExportDirectory, port=8080)
                self.addLog(f"Web server updated to directory: {self.lastExportDirectory}")

            slicer.util.infoDisplay(f"GLB export completed and Web Server updated:\n{selectedFile}")
        except Exception as e:
            slicer.util.errorDisplay(f"GLB export failed: {str(e)}")


    def onGenerateObstacleModelClicked(self):
        import slicer
        import vtk

        selectedItems = self.ui.obstacleListWidget.selectedItems()
        if len(selectedItems) < 2:
            slicer.util.errorDisplay("Select at least two segments to merge.")
            return

        inputVolume = self.ui.inputSelector.currentNode()
        if not inputVolume:
            slicer.util.errorDisplay("Nessun Input Volume selezionato.")
            return

        # 1. Crea la segmentazione di destinazione (usiamo un nome diverso per evitare conflitti col Modello 3D)
        obstacleSegmentationNode = slicer.mrmlScene.AddNewNodeByClass("vtkMRMLSegmentationNode", "ObstacleModel_Segmentation")
        obstacleSegmentationNode.CreateDefaultDisplayNodes()
        # Allinea perfettamente la griglia a quella del volume originale
        obstacleSegmentationNode.SetReferenceImageGeometryParameterFromVolumeNode(inputVolume)

        # 2. Copia i segmenti selezionati nella nuova segmentazione
        for item in selectedItems:
            segNodeID, segmentID = item.data(qt.Qt.UserRole)
            sourceSegNode = slicer.mrmlScene.GetNodeByID(segNodeID)
            if sourceSegNode:
                obstacleSegmentationNode.GetSegmentation().CopySegmentFromSegmentation(sourceSegNode.GetSegmentation(), segmentID)

        # 3. Usa il Segment Editor di Slicer "dietro le quinte" per unire i segmenti nativamente
        segmentEditorWidget = slicer.qMRMLSegmentEditorWidget()
        segmentEditorWidget.setMRMLScene(slicer.mrmlScene)
        segmentEditorNode = slicer.mrmlScene.AddNewNodeByClass("vtkMRMLSegmentEditorNode")
        segmentEditorWidget.setMRMLSegmentEditorNode(segmentEditorNode)
        segmentEditorWidget.setSegmentationNode(obstacleSegmentationNode)

        # Prendi l'elenco dei segmenti appena copiati
        copiedSegmentIDs = vtk.vtkStringArray()
        obstacleSegmentationNode.GetSegmentation().GetSegmentIDs(copiedSegmentIDs)
        
        if copiedSegmentIDs.GetNumberOfValues() > 1:
            baseSegmentID = copiedSegmentIDs.GetValue(0)
            segmentEditorWidget.setCurrentSegmentID(baseSegmentID)
            
            # Attiva l'effetto di somma logica (UNION)
            segmentEditorWidget.setActiveEffectByName("Logical operators")
            effect = segmentEditorWidget.activeEffect()
            
            # Fonde in ciclo tutti i segmenti nel primo segmento
            for i in range(1, copiedSegmentIDs.GetNumberOfValues()):
                otherSegmentID = copiedSegmentIDs.GetValue(i)
                effect.setParameter("Operation", "UNION")
                effect.setParameter("ModifierSegmentID", otherSegmentID)
                effect.self().onApply()
                
                # Rimuovi i segmenti extra dopo averli uniti
                obstacleSegmentationNode.GetSegmentation().RemoveSegment(otherSegmentID)

        # 4. Rinomina l'unico segmento rimasto
        finalSegmentIDs = vtk.vtkStringArray()
        obstacleSegmentationNode.GetSegmentation().GetSegmentIDs(finalSegmentIDs)
        if finalSegmentIDs.GetNumberOfValues() > 0:
            obstacleSegmentationNode.GetSegmentation().GetSegment(finalSegmentIDs.GetValue(0)).SetName("ObstacleModel")

        # 5. Pulizia dei nodi temporanei
        slicer.mrmlScene.RemoveNode(segmentEditorNode)
        segmentEditorWidget = None

        # 6. Esporta il risultato in un Modello 3D (creerà un vtkMRMLModelNode chiamato "ObstacleModel")
        shNode = slicer.mrmlScene.GetSubjectHierarchyNode()
        exportFolderItemId = shNode.CreateFolderItem(shNode.GetSceneItemID(), "ObstacleModels")
        slicer.modules.segmentations.logic().ExportAllSegmentsToModels(obstacleSegmentationNode, exportFolderItemId)

        # Nascondi la segmentazione, ci interessa vedere solo il modello
        obstacleSegmentationNode.GetDisplayNode().SetVisibility(False)
        for modelNode in slicer.util.getNodesByClass('vtkMRMLModelNode'):
            if modelNode.GetName() == "ObstacleModel" and modelNode.GetDisplayNode():
                modelNode.GetDisplayNode().SetVisibility(False)

        # Abilita l'interfaccia utente
        self.ui.SegmentationShow3DButton_ObstacleModel.enabled = True
        self.ui.SegmentationShow3DButton_ObstacleModel.setChecked(False)

        slicer.util.infoDisplay("Obstacle model created successfully")

    def onActivateOpenIGTLinkConnectionClicked(self, connect):
        self.updateParameterNodeFromGUI()
        
        if connect:
            # --- RICENTRAMENTO AUTOMATICO PRIMA DELLA CONNESSIONE ---
            # A questo punto la segmentazione è già completata,
            # quindi ricentrare non invalida nessuna geometria già esportata.
            inputVolume = self.ui.inputSelector.currentNode()
            if inputVolume:
                self.addLog("Ricentramento volume all'origine prima della connessione OpenIGTLink...")
                self.logic.CenterVolumeToOrigin(inputVolume)
                self.addLog("Volume ricentrato. Le slice saranno ora allineate con Unity.")
            else:
                self.addLog("ATTENZIONE: Nessun volume selezionato. Connessione senza ricentramento.")
            # ---------------------------------------------------------

            port_tracker = 18944
            status = self.logic.StartOIGTLConnection(port_tracker)
            if status == 1:
                self.isServerConnected = True
                self.ui.OIGTLconnectionLabel.text = "OpenIGTLink server - ACTIVE"
                self.addLog(f"Connessione OpenIGTLink attiva sulla porta {port_tracker}")
                self.logic.StartSliceStreaming()
                self.logic.ObserveUnityTransform()
                self.addLog("Slice streaming avviato.")
        else:
            self.logic.StopSliceStreaming()
            self.logic.StopOIGTLConnection()
            self.isServerConnected = False
            self.ui.OIGTLconnectionLabel.text = "OpenIGTLink server - INACTIVE"
            self.addLog("Connessione chiusa e streaming fermato.")
    
    #
    # Parameter node and GUI interaction
    # 			              
    def initializeParameterNode(self):
        """
        Ensure parameter node exists and observed.
        """
        # Parameter node stores all user choices in parameter values, node selections, etc.
        # so that when the scene is saved and reloaded, these settings are restored.

        self.setParameterNode(self.logic.getParameterNode())

        # Select default input nodes if nothing is selected yet to save a few clicks for the user
        if not self._parameterNode.GetNodeReference(self.logic.INPUT_VOLUME):
                firstVolumeNode = slicer.mrmlScene.GetFirstNodeByClass("vtkMRMLScalarVolumeNode")
                if firstVolumeNode:
                        self._parameterNode.SetNodeReferenceID(self.logic.INPUT_VOLUME, firstVolumeNode.GetID())

        if not self._parameterNode.GetNodeReference(self.logic.WINDOW_WIDTH):
                        self._parameterNode.SetNodeReferenceID(self.logic.WINDOW_WIDTH, "1000")                

        if not self._parameterNode.GetNodeReference(self.logic.WINDOW_LEVEL):
                        self._parameterNode.SetNodeReferenceID(self.logic.WINDOW_LEVEL, "0")  

        if not self._parameterNode.GetNodeReference(self.logic.IMAGE_HIST_SLIDEBAR_minLimit):
                self._parameterNode.SetParameter(self.logic.IMAGE_HIST_SLIDEBAR_minLimit, "500")

        if not self._parameterNode.GetNodeReference(self.logic.IMAGE_HIST_SLIDEBAR_maxLimit):
                self._parameterNode.SetParameter(self.logic.IMAGE_HIST_SLIDEBAR_maxLimit, "1500")

    def setParameterNode(self, inputParameterNode):
        """
        Set and observe parameter node.
        Observation is needed because when the parameter node is changed then the GUI must be updated immediately.
        """

        if inputParameterNode:
                self.logic.setDefaultParameters(inputParameterNode)

        # Unobserve previously selected parameter node and add an observer to the newly selected.
        # Changes of parameter node are observed so that whenever parameters are changed by a script or any other module
        # those are reflected immediately in the GUI.
        if self._parameterNode is not None:
                self.removeObserver(self._parameterNode, vtk.vtkCommand.ModifiedEvent, self.updateGUIFromParameterNode)
        self._parameterNode = inputParameterNode
        if self._parameterNode is not None:
                self.addObserver(self._parameterNode, vtk.vtkCommand.ModifiedEvent, self.updateGUIFromParameterNode)

        # Initial GUI update
        self.updateGUIFromParameterNode()

    def updateGUIFromParameterNode(self, caller=None, event=None):
        """
        This method is called whenever parameter node is changed.
        The module GUI is updated to show the current state of the parameter node.
        """

        if self._parameterNode is None or self._updatingGUIFromParameterNode:
                return

        # Make sure GUI changes do not call updateParameterNodeFromGUI (it could cause infinite loop)
        self._updatingGUIFromParameterNode = True

        # Update node selectors and sliders
        self.ui.inputSelector.setCurrentNode(self._parameterNode.GetNodeReference(self.logic.INPUT_VOLUME))
        
        # if the window level and width are set
        if self._parameterNode.GetParameter(self.logic.WINDOW_LEVEL) and self._parameterNode.GetParameter(self.logic.WINDOW_WIDTH):
            # update the WW and WL spin boxes
            WL = float(self._parameterNode.GetParameter(self.logic.WINDOW_LEVEL))
            WW = float(self._parameterNode.GetParameter(self.logic.WINDOW_WIDTH))
            self.ui.imageWWSpinBox.value = WW
            self.ui.imageWLSpinBox.value = WL
            # Update the histogram slide bar
            minVAL = WL - WW/2
            maxVAL = WL + WW/2
            self.ui.imageHistogramSlideBar.minimumValue = minVAL
            self.ui.imageHistogramSlideBar.maximumValue = maxVAL

        # All the GUI updates are done
        self._updatingGUIFromParameterNode = False

    def updateParameterNodeFromGUI(self, caller=None, event=None):
        """
        This method is called when the user makes any change in the GUI.
        The changes are saved into the parameter node (so that they are restored when the scene is saved and loaded).
        """

        if self._parameterNode is None or self._updatingGUIFromParameterNode:
                return

        wasModified = self._parameterNode.StartModify()  # Modify all properties in a single batch

        self._parameterNode.SetNodeReferenceID(self.logic.INPUT_VOLUME, self.ui.inputSelector.currentNodeID)

        self._parameterNode.SetParameter(self.logic.ACTIVE_SERVER_CHECKBOX, "true" if self.ui.serverActiveCheckBox.checked else "false")
        #self._parameterNode.SetParameter(self.logic.PATIENT_ID, (self.ui.patientID_text).text)
        #self._parameterNode.SetParameter(self.logic.USER_ID, (self.ui.userID_text).text)
        self._parameterNode.EndModify(wasModified)
    
    
    def addLog(self, text):
        """Append text to log window"""
        if hasattr(self.ui, 'statusTextBrowser'):
            self.ui.statusTextBrowser.append(text)  # Use append() instead of appendPlainText()
        else:
            # Fallback to Slicer's status bar if widget doesn't exist
            slicer.util.showStatusMessage(text, 3000)
        slicer.app.processEvents()  # force update

#
# LungNoduleBiopsyPlannerLogic
#

class LungNoduleBiopsyPlannerLogic(ScriptedLoadableModuleLogic, VTKObservationMixin):

    # Image slide
    INPUT_VOLUME = 'InputVolume'
    INPUT_VOLUME_PATH = 'InputPath'

    IMAGE_HIST_SLIDEBAR_minLimit = 'ImageHistogramSlideBar_minLimit'
    IMAGE_HIST_SLIDEBAR_maxLimit = 'ImageHistogramSlideBar_maxLimit'
    WINDOW_WIDTH = 'WindowWidth'
    WINDOW_LEVEL = 'WindowLevel'

    # OpenIGTLink connection
    ACTIVE_SERVER_CHECKBOX = 'serverActiveCheckBox'

    # Models
    MODELS_DIRECTORY = 'ModelsDirectory'
    SPINE_MODEL = 'SpineModel'
    SPINE_FILENAME = 'SpineFileName'

    # Transforms
    SPINE_TRANSFORM = 'Spine_T'

    def __init__(self):
        ScriptedLoadableModuleLogic.__init__(self)
        
        self.sliceTimer = None
        self.unityObserver = None
        self.outputSliceNode = None
        
        # Variabile per tracciare il processo CMD in background
        self.server_process = None

    def startWebServer(self, directory, port=8080):
        """Avvia il server HTTP aprendo un processo esterno separato, silenzioso e in background."""
        self.stopWebServer()  # Ferma eventuali server già attivi
        
        try:
            # Cerchiamo l'eseguibile Python nativo di Slicer
            python_exe = os.path.join(slicer.app.slicerHome, "bin", "PythonSlicer.exe")
            if not os.path.exists(python_exe): 
                python_exe = os.path.join(slicer.app.slicerHome, "bin", "PythonSlicer")
                if not os.path.exists(python_exe): 
                    python_exe = "python"

            # Nascondiamo la finestra del terminale su Windows (CREATE_NO_WINDOW)
            creation_flags = 0x08000000 if os.name == 'nt' else 0

            # Avvia il processo in background
            self.server_process = subprocess.Popen(
                # Aggiungiamo --bind 127.0.0.1 per forzare l'uso di IPv4 (evita conflitti localhost)
                [python_exe, "-m", "http.server", str(port), "--bind", "0.0.0.0"],
                cwd=directory,
                #stdout=subprocess.DEVNULL,  # <-- FIX CRITICO: Butta via i log standard
                #stderr=subprocess.DEVNULL,  # <-- FIX CRITICO: Butta via i log di errore
                creationflags=creation_flags
            )
            return port
            
        except Exception as e:
            logging.error(f"Failed to start web server subprocess: {e}")
            return None

    def stopWebServer(self):
        """Termina il processo CMD del server in background."""
        if self.server_process is not None:
            self.server_process.terminate()  # Killa il processo
            self.server_process = None

    def setDefaultParameters(self, parameterNode):
        """
        Initialize parameter node with default settings.
        """
        if not parameterNode.GetParameter(self.ACTIVE_SERVER_CHECKBOX):
                parameterNode.SetParameter(self.ACTIVE_SERVER_CHECKBOX, "0")

    def updateHistLimitsFromInput(self):
        """
        Update the min and max values of the histogram slide bar from the input volume.
        """
        parameterNode = self.getParameterNode()
        inputVolume = parameterNode.GetNodeReference(self.INPUT_VOLUME)
        # if the volume is not loaded, set the hist slidebar to 
        if inputVolume is None:
            return
        # get the image array
        imageArray = slicer.util.arrayFromVolume(inputVolume)
        # get the min and max values of the image array
        minValue = int(imageArray.min())
        maxValue = int(imageArray.max())
        # set the min and max values in the parameter node
        parameterNode.SetParameter(self.IMAGE_HIST_SLIDEBAR_minLimit, str(minValue))
        parameterNode.SetParameter(self.IMAGE_HIST_SLIDEBAR_maxLimit, str(maxValue))			

    def UpdateImageValuesWithSlider(self):
        """
        Update the window width and window level of the image according to the slider.
        """
        parameterNode = self.getParameterNode()
        # Get the CT volume we want to modify
        inputVolume = parameterNode.GetNodeReference(self.INPUT_VOLUME)
        # if the volume is not loaded, return
        if inputVolume is None:
            return
        # Get the display node of the CT volume
        displayNode = inputVolume.GetDisplayNode()

        # Get the window width and window level from the parameter node
        ww = float(parameterNode.GetParameter(self.WINDOW_WIDTH))
        wl = float(parameterNode.GetParameter(self.WINDOW_LEVEL))
        # Allow us to change the window width and window level manually
        displayNode.AutoWindowLevelOff()
        # Update the display node
        displayNode.SetWindow(ww)
        displayNode.SetLevel(wl)

        # update the parameter node
        parameterNode.SetParameter(self.WINDOW_WIDTH, str(ww))
        parameterNode.SetParameter(self.WINDOW_LEVEL, str(wl))

    def StartOIGTLConnection(self, port_tracker):
        """
        Starts OIGTL connection.
        """   
        # Open connection
        try:
                cnode = slicer.util.getNode('IGTLConnector')
        except:
                cnode = slicer.vtkMRMLIGTLConnectorNode()
                slicer.mrmlScene.AddNode(cnode)
                cnode.SetName('IGTLConnector')
        status = cnode.SetTypeServer(port_tracker)
        
        # Check connection status
        if status == 1:
                cnode.Start()
                logging.debug('Connection Successful')
                # NOTA: OutputSliceToUnity viene registrato automaticamente 
                # nella funzione SendCurrentSliceToUnity al primo invio
        else:
                print ('ERROR: Unable to activate server')
                logging.debug('ERROR: Unable to activate server')

        return status       

    def StopOIGTLConnection(self):
        """
        Stops OIGTL connection.
        """   
        cnode = slicer.util.getNode('IGTLConnector')
        cnode.Stop()
    
    def StartSliceStreaming(self):
        """
        Avvia lo streaming continuo delle slice verso Unity (stile stream_to_unity.py).
        """
        # Crea il timer se non esiste
        if self.sliceTimer is None:
            self.sliceTimer = qt.QTimer()
            self.sliceTimer.timeout.connect(self.SendCurrentSliceToUnity)
        
        # Avvia il timer (10 FPS = 100ms)
        self.sliceTimer.start(100)
        logging.info("Slice streaming timer started (10 FPS)")
    
    def StopSliceStreaming(self):
        """
        Ferma lo streaming delle slice.
        """
        if self.sliceTimer is not None:
            self.sliceTimer.stop()
            logging.info("Slice streaming timer stopped")
        
        # Rimuovi observer Unity
        if self.unityObserver is not None:
            try:
                unityTransform = slicer.mrmlScene.GetFirstNodeByName("UnityReslicePlane_T")
                if unityTransform:
                    unityTransform.RemoveObserver(self.unityObserver)
                self.unityObserver = None
            except:
                pass
    
    def SendCurrentSliceToUnity(self):
        """
        Legge l'immagine dalla SliceView corrente di Slicer e la invia a Unity.
        Usa dinamicamente i valori WW/WL impostati dall'utente nella GUI.
        """
        if self.unityObserver is None:
            self.ObserveUnityTransform()
            
        try:
            # 1. Trova connettore
            cNode = slicer.util.getNode('IGTLConnector')
            if not cNode:
                return

            # 2. Ottieni Slice Logic dalla Red view
            sliceWidget = slicer.app.layoutManager().sliceWidget("Red")
            if not sliceWidget:
                return
            
            sliceLogic = sliceWidget.sliceLogic()
            if not sliceLogic:
                return
            
            # Ottieni l'immagine grezza dalla slice view
            backgroundLayer = sliceLogic.GetBackgroundLayer()
            if not backgroundLayer:
                return
                
            reslice = backgroundLayer.GetReslice()
            if not reslice:
                return
                
            resliceOutput = reslice.GetOutput()
            if not resliceOutput:
                return

            # 3. Leggi dinamicamente i valori WW/WL dai parametri (dalla GUI)
            parameterNode = self.getParameterNode()
            windowWidth = 2559.5  # Valore di default
            windowLevel = 0       # Valore di default
            
            # Se l'utente ha impostato valori personalizzati, usali
            if parameterNode.GetParameter(self.WINDOW_WIDTH):
                try:
                    windowWidth = float(parameterNode.GetParameter(self.WINDOW_WIDTH))
                except:
                    pass
            
            if parameterNode.GetParameter(self.WINDOW_LEVEL):
                try:
                    windowLevel = float(parameterNode.GetParameter(self.WINDOW_LEVEL))
                except:
                    pass
            
            # Calcola shift e scale dinamicamente in base ai valori correnti
            shift = (windowWidth / 2.0) - windowLevel
            scale = 255.0 / windowWidth

            # 4. Conversione in 8-bit (FONDAMENTALE per Unity)
            caster = vtk.vtkImageShiftScale()
            caster.SetInputData(resliceOutput)
            caster.SetShift(shift)  # Usa il valore dinamico
            caster.SetScale(scale)  # Usa il valore dinamico
            caster.SetOutputScalarTypeToUnsignedChar()  # FORZA 8-BIT
            caster.ClampOverflowOn()  # Evita overflow
            caster.Update()
            
            finalImage = caster.GetOutput()

            # 5. Gestione Nodo Output
            if self.outputSliceNode is None:
                self.outputSliceNode = slicer.mrmlScene.GetFirstNodeByName("OutputSliceToUnity")
                if not self.outputSliceNode:
                    self.outputSliceNode = slicer.mrmlScene.AddNewNodeByClass("vtkMRMLScalarVolumeNode", "OutputSliceToUnity")
                    cNode.RegisterOutgoingMRMLNode(self.outputSliceNode)
            
            # 6. Aggiorna dati
            self.outputSliceNode.SetAndObserveImageData(finalImage)
            
            # Matrice identità per invio 2D pulito
            mat = vtk.vtkMatrix4x4()
            self.outputSliceNode.SetIJKToRASMatrix(mat)
            
            # 7. Invia
            cNode.PushNode(self.outputSliceNode)
            
        except Exception as e:
            logging.error(f"Error in SendCurrentSliceToUnity: {str(e)}")
    
    def ObserveUnityTransform(self):
        """
        Collega un observer al transform UnityReslicePlane_T per ascoltare i movimenti del piano.
        """
        # Aspetta che Unity crei il transform (potrebbe non esistere subito)
        unityTransform = slicer.mrmlScene.GetFirstNodeByName("UnityReslicePlane_T")
        
        if unityTransform:
            # Aggiungi observer per i cambiamenti
            self.unityObserver = unityTransform.AddObserver(
                slicer.vtkMRMLTransformNode.TransformModifiedEvent,
                self.OnUnityPlaneMove
            )
            logging.info("Observer attached to UnityReslicePlane_T transform")
    
    def OnUnityPlaneMove(self, caller, event):
        """
        Callback chiamato quando Unity muove il piano.
        Aggiorna la posizione della SliceView di Slicer per mostrare la slice corrispondente.
        """
        try:
            # Ottieni il volume di input
            parameterNode = self.getParameterNode()
            inputVolume = parameterNode.GetNodeReference(self.INPUT_VOLUME)
            if not inputVolume:
                return
            
            # Ottieni il transform Unity
            unityTransform = caller  # Il caller è il nodo transform
            
            # Estrai la matrice di trasformazione
            transformMatrix = vtk.vtkMatrix4x4()
            unityTransform.GetMatrixTransformToWorld(transformMatrix)
            
            # Estrai la posizione (traslazione) - elemento [2][3] è la Z in RAS
            position = [transformMatrix.GetElement(0, 3),
                        transformMatrix.GetElement(1, 3),
                        transformMatrix.GetElement(2, 3)]
            
            # Aggiorna la SliceView Red per mostrare questa posizione
            sliceNode = slicer.util.getNode('vtkMRMLSliceNodeRed')
            if sliceNode:
                # Imposta la slice view sulla posizione Z del piano Unity
                sliceNode.SetSliceOrigin(position[0], position[1], position[2])
                
        except Exception as e:
            logging.error(f"Error in OnUnityPlaneMove: {str(e)}")
    
    def CenterVolumeToOrigin(self, volumeNode):
        """
        Ricentra un volume MRML in modo che il suo centro geometrico 
        corrisponda all'origine (0,0,0) del sistema di coordinate RAS.
        Questo garantisce perfetto allineamento con Unity.
        Implementazione identica a center_nifti_volume() in auto_segmentation.py.
        """
        if not volumeNode:
            return
        
        try:
            # Ottieni le dimensioni del volume
            imageData = volumeNode.GetImageData()
            if not imageData:
                return
            
            dimensions = imageData.GetDimensions()
            
            # Calcola il centro in coordinate voxel (IJK)
            centerVoxel = np.array([
                (dimensions[0] - 1) / 2.0,
                (dimensions[1] - 1) / 2.0,
                (dimensions[2] - 1) / 2.0
            ])
            
            # Ottieni la matrice IJK to RAS corrente
            ijkToRas = vtk.vtkMatrix4x4()
            volumeNode.GetIJKToRASMatrix(ijkToRas)
            
            # Estrai la sotto-matrice 3x3 di rotazione/scala
            rotation_scale = np.zeros((3, 3))
            for i in range(3):
                for j in range(3):
                    rotation_scale[i, j] = ijkToRas.GetElement(i, j)
            
            # Calcola l'offset del centro in coordinate RAS
            centerWorldOffset = rotation_scale.dot(centerVoxel)
            
            # Modifica la matrice per ricentrare all'origine
            # Impostiamo la colonna di traslazione (4a colonna) a -centerWorldOffset
            ijkToRas.SetElement(0, 3, -centerWorldOffset[0])
            ijkToRas.SetElement(1, 3, -centerWorldOffset[1])
            ijkToRas.SetElement(2, 3, -centerWorldOffset[2])
            
            # Applica la nuova matrice al volume
            volumeNode.SetIJKToRASMatrix(ijkToRas)
            
            # IMPORTANTE: Ricentra anche le slice view all'origine (0,0,0)
            # Altrimenti rimangono nella posizione originale e sembrano "spostate"
            self.ResetSliceViewsToCenter()
            
            logging.info(f"Volume {volumeNode.GetName()} centered to origin (0,0,0)")
            logging.info(f"  Dimensions: {dimensions}")
            logging.info(f"  Center offset removed: [{centerWorldOffset[0]:.2f}, {centerWorldOffset[1]:.2f}, {centerWorldOffset[2]:.2f}] mm")
            
        except Exception as e:
            logging.error(f"Error centering volume: {str(e)}")
            import traceback
            traceback.print_exc()
    
    def ResetSliceViewsToCenter(self):
        """
        Ricentra tutte le slice view (Red, Yellow, Green) all'origine (0,0,0).
        Chiamato dopo aver ricentrato il volume per evitare che le slice
        sembrino "spostate" rispetto al volume.
        """
        try:
            # Ricentra Red view
            redSliceNode = slicer.util.getNode('vtkMRMLSliceNodeRed')
            if redSliceNode:
                redSliceNode.SetSliceOrigin(0, 0, 0)
            
            # Ricentra Yellow view
            yellowSliceNode = slicer.util.getNode('vtkMRMLSliceNodeYellow')
            if yellowSliceNode:
                yellowSliceNode.SetSliceOrigin(0, 0, 0)
            
            # Ricentra Green view
            greenSliceNode = slicer.util.getNode('vtkMRMLSliceNodeGreen')
            if greenSliceNode:
                greenSliceNode.SetSliceOrigin(0, 0, 0)
            
            # Opzionale: resetta anche il campo visivo per adattarlo al volume
            slicer.util.resetSliceViews()
            
            logging.info("Slice views reset to origin (0,0,0)")
            
        except Exception as e:
            logging.error(f"Error resetting slice views: {str(e)}")
    
    # Save functionality managed by AR_Planner module
    # Uncomment if implementing save in this module:
    '''def SaveData(self):
        """
        Save the data in the scene.
        """ 
        parameterNode = self.getParameterNode()

        # Get the Save Directory from slicer settings
        settings = slicer.app.userSettings()
        saveDirectory = settings.value(self.SAVING_DIRECTORY)

        patientID = parameterNode.GetParameter(self.PATIENT_ID)
        userID = parameterNode.GetParameter(self.USER_ID)
        spineModel_node = parameterNode.GetNodeReference(self.SPINE_MODEL)
        spineT_node = parameterNode.GetNodeReference(self.SPINE_TRANSFORM)

        # Extract data data
        currentDate = time.strftime("%Y-%m-%d_%H-%M-%S")
        
        # Saving folder path
        save_folder_path = os.path.join(saveDirectory, "Patient_00" + patientID, "User_" + userID)
        
        # Create the saving folder if it doesn't exist
        if not (os.path.exists(save_folder_path)):
                os.makedirs(save_folder_path)


        ## Save the scene
        # Generate file name
        sceneName = "{}_{}_patient{}_user{}".format(currentDate, "Scene", patientID, userID)
        sceneSaveFilename = os.path.join(save_folder_path, sceneName + ".mrb")
        # Save scene
        if slicer.util.saveScene(sceneSaveFilename):
            logging.info("Scene saved to: {0}".format(sceneSaveFilename))
        else:
            logging.error("Scene saving failed")
        
        return save_folder_path'''
    
    @staticmethod
    def decimate(inputModel, outputModel, reductionFactor=0.8, decimateBoundary=True, lossless=False, aggressiveness=7.0):
        """Perform a topology-preserving reduction of surface triangles. FastMesh method uses Sven Forstmann's method
        (https://github.com/sp4cerat/Fast-Quadric-Mesh-Simplification).

        :param reductionFactor: Target reduction factor during decimation. Ratio of triangles that are requested to
        be eliminated. 0.8 means that the mesh size is requested to be reduced by 80%.
        :param decimateBoundary: If enabled then 'FastQuadric' method is used (it provides more even element sizes but cannot
        be forced to preserve boundary), otherwise 'DecimatePro' method is used (that can preserve boundary edges but tend
        to create more ill-shaped triangles).
        :param lossless: Lossless remeshing for FastQuadric method. The flag has no effect if other method is used.
        :param aggressiveness: Balances between accuracy and computation time for FastQuadric method (default = 7.0). The flag has no effect if other method is used.
        """
        parameters = {
        "inputModel": inputModel,
        "outputModel": outputModel,
        "reductionFactor": reductionFactor,
        "method": "FastQuadric" if decimateBoundary else "DecimatePro",
        "boundaryDeletion": decimateBoundary
        }
        cliNode = slicer.cli.runSync(slicer.modules.decimation, None, parameters)
        slicer.mrmlScene.RemoveNode(cliNode)

    @staticmethod
    def smoothModel(inputModel, outputModel=None, iterations=30, passBand=0.08):
        """
        Smoothing della mesh tramite vtkWindowedSincPolyDataFilter.
        Utile per rimuovere l'effetto 'a gradini' delle superfici derivate da labelmap.
        """
        if outputModel is None:
            outputModel = inputModel

        polyData = inputModel.GetPolyData()
        if polyData is None or polyData.GetNumberOfPoints() == 0:
            return

        smoother = vtk.vtkWindowedSincPolyDataFilter()
        smoother.SetInputData(polyData)
        smoother.SetNumberOfIterations(iterations)
        smoother.SetPassBand(passBand)
        smoother.BoundarySmoothingOff()
        smoother.FeatureEdgeSmoothingOff()
        smoother.NonManifoldSmoothingOn()
        smoother.NormalizeCoordinatesOn()
        smoother.Update()

        normals = vtk.vtkPolyDataNormals()
        normals.SetInputConnection(smoother.GetOutputPort())
        normals.SetFeatureAngle(80.0)
        normals.SplittingOff()
        normals.ConsistencyOn()
        normals.AutoOrientNormalsOn()
        normals.Update()

        outputModel.SetAndObservePolyData(normals.GetOutput())
        outputModel.Modified()