Option Strict On

Imports System.IO
Imports PHRPReader
Imports ProteinCoverageSummarizer
Imports ProteinFileReader

' This class uses ProteinCoverageSummarizer.dll to read in a protein fasta file or delimited protein info file along with
' an accompanying file with peptide sequences to find the proteins that contain each peptide
' It will also optionally compute the percent coverage of each of the proteins
'
' -------------------------------------------------------------------------------
' Written by Matthew Monroe for the Department of Energy (PNNL, Richland, WA)
' Program started September 27, 2008
'
' E-mail: matthew.monroe@pnl.gov or matt@alchemistmatt.com
' Website: http://ncrr.pnl.gov/ or http://www.sysbio.org/resources/staff/
' -------------------------------------------------------------------------------
' 
' Licensed under the Apache License, Version 2.0; you may not use this file except
' in compliance with the License.  You may obtain a copy of the License at 
' http://www.apache.org/licenses/LICENSE-2.0
'
' Notice: This computer software was prepared by Battelle Memorial Institute, 
' hereinafter the Contractor, under Contract No. DE-AC05-76RL0 1830 with the 
' Department of Energy (DOE).  All rights in the computer software are reserved 
' by DOE on behalf of the United States Government and the Contractor as 
' provided in the Contract.  NEITHER THE GOVERNMENT NOR THE CONTRACTOR MAKES ANY 
' WARRANTY, EXPRESS OR IMPLIED, OR ASSUMES ANY LIABILITY FOR THE USE OF THIS 
' SOFTWARE.  This notice including this sentence must appear on any copies of 
' this computer software.

Public Class clsPeptideToProteinMapEngine
    Inherits clsProcessFilesBaseClass

    Public Sub New()
        InitializeVariables()
    End Sub

#Region "Constants and Enums"

    Public Const FILENAME_SUFFIX_INSPECT_RESULTS_FILE As String = "_inspect.txt"
    Public Const FILENAME_SUFFIX_MSGFDB_RESULTS_FILE As String = "_msgfdb.txt"

    Public Const FILENAME_SUFFIX_PEP_TO_PROTEIN_MAPPING As String = "_PepToProtMap.txt"

    Protected Const FILENAME_SUFFIX_PSM_UNIQUE_PEPTIDES As String = "_peptides"

    ' The following are the initial % complete value displayed during each of these stages
    Protected Const PERCENT_COMPLETE_PREPROCESSING As Single = 0
    Protected Const PERCENT_COMPLETE_RUNNING_PROTEIN_COVERAGE_SUMMARIZER As Single = 5
    Protected Const PERCENT_COMPLETE_POSTPROCESSING As Single = 95

    Public Enum eProteinCoverageErrorCodes
        NoError = 0
        UnspecifiedError = -1
    End Enum

    Public Enum ePeptideInputFileFormatConstants
        Unknown = -1
        AutoDetermine = 0
        PeptideListFile = 1             ' First column is peptide sequence
        ProteinAndPeptideFile = 2       ' First column is protein name, second column is peptide sequence
        InspectResultsFile = 3          ' Inspect results file; pre-process the file to determine the peptides present, then determine the proteins that contain the given peptides
        MSGFDBResultsFile = 4           ' MSGF-DB results file; pre-process the file to determine the peptides present, then determine the proteins that contain the given peptides
        PHRPFile = 5                    ' Sequest, Inspect, X!Tandem, or MSGF-DB synopsis or first-hits file created by PHRP; pre-process the file to determine the peptides present, then determine the proteins that contain the given peptides
    End Enum

#End Region

#Region "Structures"

    Protected Structure udtProteinIDMapInfoType
        Public ProteinID As Integer
        Public Peptide As String
        Public ResidueStart As Integer
        Public ResidueEnd As Integer
    End Structure

    Protected Structure udtPepToProteinMappingType
        Public Peptide As String
        Public Protein As String
        Public ResidueStart As Integer
        Public ResidueEnd As Integer
    End Structure
#End Region

#Region "Classwide variables"
    Protected WithEvents mProteinCoverageSummarizer As clsProteinCoverageSummarizer

    Private mPeptideInputFileFormat As ePeptideInputFileFormatConstants
    Private mDeleteTempFiles As Boolean

    ' When processing an inspect search result file, if you provide the inspect parameter file name, 
    '  then this program will read the parameter file and look for the "mod," lines.  The user-assigned mod
    '  names will be extracted and used when "cleaning" the peptides prior to looking for matching proteins
    Private mInspectParameterFilePath As String

    Private mLocalErrorCode As eProteinCoverageErrorCodes
    Private mStatusMessage As String

    ' The following is used when the input file is Sequest, X!Tandem, Inspect, or MSGF-DB results file
    ' Keys are peptide sequences; values are Lists of scan numbers that each peptide was observed in
    ' Keys may have mod symbols in them; those symbols will be removed in PreProcessDataWriteOutPeptides
    Private mUniquePeptideList As SortedList(Of String, SortedSet(Of Integer))

    ' Mod names must be lower case, and 4 characters in length (or shorter)
    ' Only used with Inspect since mods in MSGF-DB are simply numbers, e.g. R.DNFM+15.995SATQAVEYGLVDAVM+15.995TK.R
    '  while mods in Sequest and XTandem are symbols (*, #, @)
    Private mInspectModNameList As List(Of String)

#End Region

#Region "Properties"

    ''' <summary>
    ''' Legacy property; superseded by DeleteTempFiles
    ''' </summary>
    ''' <value></value>
    ''' <returns></returns>
    ''' <remarks></remarks>
    Public Property DeleteInspectTempFiles() As Boolean
        Get
            Return Me.DeleteTempFiles
        End Get
        Set(value As Boolean)
            Me.DeleteTempFiles = value
        End Set
    End Property

    Public Property DeleteTempFiles() As Boolean
        Get
            Return mDeleteTempFiles
        End Get
        Set(value As Boolean)
            mDeleteTempFiles = value
        End Set
    End Property

    Public Property IgnoreILDifferences() As Boolean
        Get
            Return mProteinCoverageSummarizer.IgnoreILDifferences
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.IgnoreILDifferences = Value
        End Set
    End Property

    Public Property InspectParameterFilePath() As String
        Get
            Return mInspectParameterFilePath
        End Get
        Set(value As String)
            If value Is Nothing Then value = String.Empty
            mInspectParameterFilePath = value
        End Set
    End Property

    Public Property MatchPeptidePrefixAndSuffixToProtein() As Boolean
        Get
            Return mProteinCoverageSummarizer.MatchPeptidePrefixAndSuffixToProtein
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.MatchPeptidePrefixAndSuffixToProtein = Value
        End Set
    End Property

    Public Property OutputProteinSequence() As Boolean
        Get
            Return mProteinCoverageSummarizer.OutputProteinSequence
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.OutputProteinSequence = Value
        End Set
    End Property

    Public Property PeptideFileSkipFirstLine() As Boolean
        Get
            Return mProteinCoverageSummarizer.PeptideFileSkipFirstLine
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.PeptideFileSkipFirstLine = Value
        End Set
    End Property

    Public Property PeptideInputFileDelimiter() As Char
        Get
            Return mProteinCoverageSummarizer.PeptideInputFileDelimiter
        End Get
        Set(Value As Char)
            mProteinCoverageSummarizer.PeptideInputFileDelimiter = Value
        End Set
    End Property

    Public Property PeptideInputFileFormat() As ePeptideInputFileFormatConstants
        Get
            Return mPeptideInputFileFormat
        End Get
        Set(value As ePeptideInputFileFormatConstants)
            mPeptideInputFileFormat = value
        End Set
    End Property

    Public Property ProteinDataDelimitedFileDelimiter() As Char
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileDelimiter
        End Get
        Set(value As Char)
            mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileDelimiter = value
        End Set
    End Property
    Public Property ProteinDataDelimitedFileFormatCode() As DelimitedFileReader.eDelimitedFileFormatCode
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileFormatCode
        End Get
        Set(value As DelimitedFileReader.eDelimitedFileFormatCode)
            mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileFormatCode = value
        End Set
    End Property
    Public Property ProteinDataDelimitedFileSkipFirstLine() As Boolean
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileSkipFirstLine
        End Get
        Set(value As Boolean)
            mProteinCoverageSummarizer.mProteinDataCache.DelimitedFileSkipFirstLine = value
        End Set
    End Property
    Public Property ProteinDataRemoveSymbolCharacters() As Boolean
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.RemoveSymbolCharacters
        End Get
        Set(value As Boolean)
            mProteinCoverageSummarizer.mProteinDataCache.RemoveSymbolCharacters = value
        End Set
    End Property
    Public Property ProteinDataIgnoreILDifferences() As Boolean
        Get
            Return mProteinCoverageSummarizer.mProteinDataCache.IgnoreILDifferences
        End Get
        Set(value As Boolean)
            mProteinCoverageSummarizer.mProteinDataCache.IgnoreILDifferences = value
        End Set
    End Property

    Public Property ProteinInputFilePath() As String
        Get
            Return mProteinCoverageSummarizer.ProteinInputFilePath
        End Get
        Set(Value As String)
            If Value Is Nothing Then Value = String.Empty
            mProteinCoverageSummarizer.ProteinInputFilePath = Value
        End Set
    End Property

    Public ReadOnly Property ProteinToPeptideMappingFilePath() As String
        Get
            Return mProteinCoverageSummarizer.ProteinToPeptideMappingFilePath
        End Get
    End Property

    Public Property RemoveSymbolCharacters() As Boolean
        Get
            Return mProteinCoverageSummarizer.RemoveSymbolCharacters
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.RemoveSymbolCharacters = Value
        End Set
    End Property

    Public ReadOnly Property ResultsFilePath() As String
        Get
            Return mProteinCoverageSummarizer.ResultsFilePath
        End Get
    End Property

    Public Property SaveProteinToPeptideMappingFile() As Boolean
        Get
            Return mProteinCoverageSummarizer.SaveProteinToPeptideMappingFile
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.SaveProteinToPeptideMappingFile = Value
        End Set
    End Property

    Public Property SaveSourceDataPlusProteinsFile() As Boolean
        Get
            Return mProteinCoverageSummarizer.SaveSourceDataPlusProteinsFile
        End Get
        Set(value As Boolean)
            mProteinCoverageSummarizer.SaveSourceDataPlusProteinsFile = value
        End Set
    End Property

    Public Property SearchAllProteinsForPeptideSequence() As Boolean
        Get
            Return mProteinCoverageSummarizer.SearchAllProteinsForPeptideSequence
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.SearchAllProteinsForPeptideSequence = Value
        End Set
    End Property

    Public Property UseLeaderSequenceHashTable() As Boolean
        Get
            Return mProteinCoverageSummarizer.UseLeaderSequenceHashTable
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.UseLeaderSequenceHashTable = Value
        End Set
    End Property

    Public Property SearchAllProteinsSkipCoverageComputationSteps() As Boolean
        Get
            Return mProteinCoverageSummarizer.SearchAllProteinsSkipCoverageComputationSteps
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.SearchAllProteinsSkipCoverageComputationSteps = Value
        End Set
    End Property

    Public ReadOnly Property StatusMessage() As String
        Get
            Return mStatusMessage
        End Get
    End Property

    Public Property TrackPeptideCounts() As Boolean
        Get
            Return mProteinCoverageSummarizer.TrackPeptideCounts
        End Get
        Set(Value As Boolean)
            mProteinCoverageSummarizer.TrackPeptideCounts = Value
        End Set
    End Property

#End Region

    Public Overrides Sub AbortProcessingNow()
        MyBase.AbortProcessingNow()
        If Not mProteinCoverageSummarizer Is Nothing Then
            mProteinCoverageSummarizer.AbortProcessingNow()
        End If
    End Sub

    Public Function DetermineResultsFileFormat(strFilePath As String) As ePeptideInputFileFormatConstants
        ' Examine the strFilePath to determine the file format

        If Path.GetFileName(strFilePath).ToLower.EndsWith(FILENAME_SUFFIX_INSPECT_RESULTS_FILE.ToLower()) Then
            Return ePeptideInputFileFormatConstants.InspectResultsFile

        ElseIf Path.GetFileName(strFilePath).ToLower.EndsWith(FILENAME_SUFFIX_MSGFDB_RESULTS_FILE.ToLower()) Then
            Return ePeptideInputFileFormatConstants.MSGFDBResultsFile

        ElseIf mPeptideInputFileFormat <> ePeptideInputFileFormatConstants.AutoDetermine And mPeptideInputFileFormat <> ePeptideInputFileFormatConstants.Unknown Then
            Return mPeptideInputFileFormat
        End If

        Dim strBaseNameLCase As String = Path.GetFileNameWithoutExtension(strFilePath)
        If strBaseNameLCase.EndsWith("_msgfdb") OrElse strBaseNameLCase.EndsWith("_msgfplus") Then
            Return ePeptideInputFileFormatConstants.MSGFDBResultsFile
        End If

        Dim eResultType As clsPHRPReader.ePeptideHitResultType
        eResultType = clsPHRPReader.AutoDetermineResultType(strFilePath)
        If eResultType <> clsPHRPReader.ePeptideHitResultType.Unknown Then
            Return ePeptideInputFileFormatConstants.PHRPFile
        End If

        ShowMessage("Unable to determine the format of the input file based on the filename suffix; will assume the first column contains peptide sequence")
        Return ePeptideInputFileFormatConstants.PeptideListFile

    End Function

    Public Function ExtractModInfoFromInspectParamFile(strInspectParameterFilePath As String, ByRef lstInspectModNames As List(Of String)) As Boolean

        Dim strLineIn As String
        Dim strSplitLine As String()

        Dim intCurrentLine As Integer

        Dim blnSuccess = False

        Try

            If lstInspectModNames Is Nothing Then
                lstInspectModNames = New List(Of String)
            Else
                lstInspectModNames.Clear()
            End If

            If strInspectParameterFilePath Is Nothing OrElse strInspectParameterFilePath.Length = 0 Then
                Return False
            End If

            ShowMessage("Looking for mod definitions in the Inspect param file: " & Path.GetFileName(strInspectParameterFilePath))

            ' Read the contents of strProteinToPeptideMappingFilePath
            Using srInFile = New StreamReader(New FileStream(strInspectParameterFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))

                intCurrentLine = 1
                Do While Not srInFile.EndOfStream
                    strLineIn = srInFile.ReadLine

                    strLineIn = strLineIn.Trim

                    If strLineIn.Length > 0 Then

                        If strLineIn.Chars(0) = "#"c Then
                            ' Comment line; skip it
                        ElseIf strLineIn.ToLower.StartsWith("mod") Then
                            ' Modification definition line

                            ' Split the line on commas
                            strSplitLine = strLineIn.Split(","c)

                            If strSplitLine.Length >= 5 AndAlso strSplitLine(0).ToLower.Trim = "mod" Then

                                Dim strModName As String
                                strModName = strSplitLine(4).ToLower()

                                If strModName.Length > 4 Then
                                    ' Only keep the first 4 characters of the modification name
                                    strModName = strModName.Substring(0, 4)
                                End If

                                lstInspectModNames.Add(strModName)
                                ShowMessage("Found modification: " & strLineIn & "   -->   Mod Symbol """ & strModName & """")

                            End If
                        End If
                    End If
                    intCurrentLine += 1

                Loop

            End Using

            Console.WriteLine()

            blnSuccess = True

        Catch ex As Exception
            mStatusMessage = "Error reading the Inspect parameter file (" & Path.GetFileName(strInspectParameterFilePath) & ")"
            HandleException(mStatusMessage, ex)
        End Try

        Return blnSuccess

    End Function

    Public Overrides Function GetErrorMessage() As String
        Return MyBase.GetBaseClassErrorMessage
    End Function

    Private Sub InitializeVariables()
        Me.ShowMessages = True

        mPeptideInputFileFormat = ePeptideInputFileFormatConstants.AutoDetermine
        mDeleteTempFiles = True

        mInspectParameterFilePath = String.Empty

        mAbortProcessing = False
        mStatusMessage = String.Empty

        mProteinCoverageSummarizer = New clsProteinCoverageSummarizer

        mInspectModNameList = New List(Of String)

        mUniquePeptideList = New SortedList(Of String, SortedSet(Of Integer))
    End Sub

    ''' <summary>
    ''' Open the file and read the first line
    ''' Examine it to determine if it looks like a header line
    ''' </summary>
    ''' <returns></returns>
    Private Function IsHeaderLinePresent(filePath As String, eInputFileFormat As ePeptideInputFileFormatConstants) As Boolean

        Dim chSepChars = New Char() {ControlChars.Tab}

        Try
            Dim headerFound = False

            ' Read the contents of strProteinToPeptideMappingFilePath
            Using srInFile = New StreamReader(New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))

                If Not srInFile.EndOfStream Then
                    Dim dataLine = srInFile.ReadLine()

                    If Not String.IsNullOrEmpty(dataLine) Then
                        Dim dataCols = dataLine.Split(chSepChars)

                        If eInputFileFormat = ePeptideInputFileFormatConstants.ProteinAndPeptideFile Then
                            If dataCols.Length > 1 AndAlso dataCols(1).StartsWith("peptide", StringComparison.InvariantCultureIgnoreCase) Then
                                headerFound = True
                            End If
                        ElseIf eInputFileFormat = ePeptideInputFileFormatConstants.PeptideListFile Then
                            If dataCols(0).StartsWith("peptide", StringComparison.InvariantCultureIgnoreCase) Then
                                headerFound = True
                            End If
                        Else
                            If dataCols.Any(Function(dataColumn) dataColumn.ToLower().StartsWith("peptide")) Then
                                headerFound = True
                            End If
                        End If
                    End If
                End If

            End Using

            Return headerFound

        Catch ex As Exception
            mStatusMessage = "Error looking for a header line in " & Path.GetFileName(filePath)
            HandleException(mStatusMessage, ex)
            Return False
        End Try

    End Function
    
    Public Function LoadParameterFileSettings(strParameterFilePath As String) As Boolean
        Return mProteinCoverageSummarizer.LoadParameterFileSettings(strParameterFilePath)
    End Function

    Protected Function PostProcessPSMResultsFile(strPeptideListFilePath As String,
       strProteinToPeptideMappingFilePath As String,
       blnDeleteWorkingFiles As Boolean) As Boolean

        Const UNKNOWN_PROTEIN_NAME = "__NoMatch__"

        Dim strPeptideToProteinMappingFilePath As String

        Dim strCleanSequence As String
        Dim strProtein As String

        Dim chPrefixResidue As Char
        Dim chSuffixResidue As Char

        Dim intMatchIndex As Integer
        Dim intProteinIDMatchIndex As Integer

        Dim objProteinMapPeptideComparer As ProteinIDMapInfoPeptideSearchComparer

        Dim strProteins() As String
        Dim intProteinIDPointerArray() As Integer

        Dim udtProteinMapInfo() As udtProteinIDMapInfoType

        Dim lstCachedData As List(Of udtPepToProteinMappingType)
        Dim objCachedDataComparer As PepToProteinMappingComparer

        Dim blnSuccess = False

        Try
            Console.WriteLine()
            Console.WriteLine()

            ShowMessage("Post-processing the results files")

            If mUniquePeptideList Is Nothing OrElse mUniquePeptideList.Count = 0 Then
                mStatusMessage = "Error in PostProcessPSMResultsFile: mUniquePeptideList is empty; this is unexpected; unable to continue"

                HandleException(mStatusMessage, New Exception("Empty Array"))

                Return False
            End If

            ReDim strProteins(0)
            ReDim intProteinIDPointerArray(0)
            ReDim udtProteinMapInfo(0)

            blnSuccess = PostProcessPSMResultsFileReadMapFile(strProteinToPeptideMappingFilePath,
              strProteins, intProteinIDPointerArray, udtProteinMapInfo)

            ' Sort udtProteinMapInfo on peptide, then on protein
            Array.Sort(udtProteinMapInfo, New ProteinIDMapInfoComparer)

            ' Create the final result file
            If strProteinToPeptideMappingFilePath.Contains(FILENAME_SUFFIX_PSM_UNIQUE_PEPTIDES & clsProteinCoverageSummarizer.FILENAME_SUFFIX_PROTEIN_TO_PEPTIDE_MAPPING) Then
                ' This was an old name format that is no longer used
                ' This code block should, therefore, never be reached
                strPeptideToProteinMappingFilePath = strProteinToPeptideMappingFilePath.Replace(
                    FILENAME_SUFFIX_PSM_UNIQUE_PEPTIDES & clsProteinCoverageSummarizer.FILENAME_SUFFIX_PROTEIN_TO_PEPTIDE_MAPPING,
                    FILENAME_SUFFIX_PEP_TO_PROTEIN_MAPPING)
            Else
                strPeptideToProteinMappingFilePath = strProteinToPeptideMappingFilePath.Replace(
                    clsProteinCoverageSummarizer.FILENAME_SUFFIX_PROTEIN_TO_PEPTIDE_MAPPING,
                    FILENAME_SUFFIX_PEP_TO_PROTEIN_MAPPING)

                If String.Equals(strProteinToPeptideMappingFilePath, strPeptideToProteinMappingFilePath) Then
                    ' The filename was not in the exacted format
                    strPeptideToProteinMappingFilePath = clsProteinCoverageSummarizer.ConstructOutputFilePath(
                        strProteinToPeptideMappingFilePath, FILENAME_SUFFIX_PEP_TO_PROTEIN_MAPPING,
                        Path.GetDirectoryName(strProteinToPeptideMappingFilePath), "")
                End If
            End If

            LogMessage("Creating " & Path.GetFileName(strPeptideToProteinMappingFilePath))

            Using swOutFile = New StreamWriter(New FileStream(strPeptideToProteinMappingFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))

                ' Write the headers
                swOutFile.WriteLine("Peptide" & ControlChars.Tab &
                  "Protein" & ControlChars.Tab &
                  "Residue_Start" & ControlChars.Tab &
                  "Residue_End")

                ' Initialize the Binary Search comparer
                objProteinMapPeptideComparer = New ProteinIDMapInfoPeptideSearchComparer()


                ' Assure that intProteinIDPointerArray and strProteins are sorted in parallel
                Array.Sort(intProteinIDPointerArray, strProteins)

                ' Initialize lstCachedData
                lstCachedData = New List(Of udtPepToProteinMappingType)

                ' Initialize objCachedDataComparer
                objCachedDataComparer = New PepToProteinMappingComparer

                For Each strPeptideEntry In mUniquePeptideList

                    ' Construct the clean sequence for this peptide
                    strCleanSequence = clsProteinCoverageSummarizer.GetCleanPeptideSequence(
                     strPeptideEntry.Key,
                     chPrefixResidue,
                     chSuffixResidue,
                     mProteinCoverageSummarizer.RemoveSymbolCharacters)

                    If mInspectModNameList.Count > 0 Then
                        strCleanSequence = RemoveInspectMods(strCleanSequence, mInspectModNameList)
                    End If

                    ' Look for strCleanSequence in udtProteinMapInfo
                    intMatchIndex = Array.BinarySearch(udtProteinMapInfo, strCleanSequence, objProteinMapPeptideComparer)

                    If intMatchIndex < 0 Then
                        ' Match not found; this is unexpected
                        ' However, this code will be reached if the peptide is not present in any of the proteins in the protein data file
                        swOutFile.WriteLine(strPeptideEntry.Key & ControlChars.Tab &
                         UNKNOWN_PROTEIN_NAME & ControlChars.Tab &
                          0.ToString & ControlChars.Tab &
                          0.ToString)

                    Else
                        ' Decrement intMatchIndex until the first match in udtProteinMapInfo is found
                        Do While intMatchIndex > 0 AndAlso udtProteinMapInfo(intMatchIndex - 1).Peptide = strCleanSequence
                            intMatchIndex -= 1
                        Loop

                        ' Now write out each of the proteins for this peptide
                        ' We're caching results to lstCachedData so that we can sort by protein name
                        lstCachedData.Clear()

                        Do
                            ' Find the Protein for ID udtProteinMapInfo(intMatchIndex).ProteinID
                            intProteinIDMatchIndex = Array.BinarySearch(intProteinIDPointerArray, udtProteinMapInfo(intMatchIndex).ProteinID)

                            If intProteinIDMatchIndex >= 0 Then
                                strProtein = strProteins(intProteinIDMatchIndex)
                            Else
                                strProtein = UNKNOWN_PROTEIN_NAME
                            End If

                            Try
                                If strProtein <> strProteins(udtProteinMapInfo(intMatchIndex).ProteinID) Then
                                    ' This is unexpected
                                    ShowMessage("Warning: Unexpected protein ID lookup array mismatch for ID " & udtProteinMapInfo(intMatchIndex).ProteinID.ToString)
                                End If
                            Catch ex As Exception
                                ' This code shouldn't be reached
                                ' Ignore errors occur
                            End Try

                            Dim udtCachedDataEntry = New udtPepToProteinMappingType With {
                                .Peptide = String.Copy(strPeptideEntry.Key),
                                .Protein = String.Copy(strProtein),
                                .ResidueStart = udtProteinMapInfo(intMatchIndex).ResidueStart,
                                .ResidueEnd = udtProteinMapInfo(intMatchIndex).ResidueEnd
                            }

                            lstCachedData.Add(udtCachedDataEntry)

                            intMatchIndex += 1
                        Loop While intMatchIndex < udtProteinMapInfo.Length AndAlso udtProteinMapInfo(intMatchIndex).Peptide = strCleanSequence

                        If lstCachedData.Count > 1 Then
                            lstCachedData.Sort(objCachedDataComparer)
                        End If

                        For intCacheIndex = 0 To lstCachedData.Count - 1
                            With lstCachedData(intCacheIndex)
                                swOutFile.WriteLine(.Peptide & ControlChars.Tab &
                                  .Protein & ControlChars.Tab &
                                  .ResidueStart.ToString & ControlChars.Tab &
                                  .ResidueEnd.ToString)

                            End With

                        Next
                    End If

                Next

            End Using

            If blnDeleteWorkingFiles Then
                Try
                    LogMessage("Deleting " & Path.GetFileName(strPeptideListFilePath))
                    File.Delete(strPeptideListFilePath)
                Catch ex As Exception
                End Try

                Try
                    LogMessage("Deleting " & Path.GetFileName(strProteinToPeptideMappingFilePath))
                    File.Delete(strProteinToPeptideMappingFilePath)
                Catch ex As Exception
                End Try

            End If

            blnSuccess = True

        Catch ex As Exception
            mStatusMessage = "Error writing the Inspect or MSGF-DB peptide to protein map file in PostProcessPSMResultsFile"
            HandleException(mStatusMessage, ex)
        End Try

        Return blnSuccess

    End Function

    Protected Function PostProcessPSMResultsFileReadMapFile(strProteinToPeptideMappingFilePath As String,
      ByRef strProteins() As String,
      ByRef intProteinIDPointerArray() As Integer,
      ByRef udtProteinMapInfo() As udtProteinIDMapInfoType) As Boolean

        Dim intTerminatorSize = 2

        Dim strLineIn As String
        Dim strSplitLine As String()

        Dim intCurrentLine As Integer
        Dim bytesRead As Long = 0

        Dim dctProteinList As Dictionary(Of String, Integer)

        Dim intProteinMapInfoCount As Integer

        Dim strCurrentProtein As String
        Dim intCurrentProteinID As Integer

        Dim blnSuccess = False

        Try

            ' Initialize the protein list dictionary
            dctProteinList = New Dictionary(Of String, Integer)

            ' Initialize the protein to peptide mapping array
            ' We know the length will be at least as long as mUniquePeptideList, and easily twice that length
            ReDim udtProteinMapInfo(mUniquePeptideList.Count * 2 - 1)

            LogMessage("Reading " & Path.GetFileName(strProteinToPeptideMappingFilePath))

            ' Read the contents of strProteinToPeptideMappingFilePath
            Using srInFile = New StreamReader(New FileStream(strProteinToPeptideMappingFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))

                strCurrentProtein = String.Empty

                intCurrentLine = 1
                Do While Not srInFile.EndOfStream
                    If mAbortProcessing Then Exit Do

                    strLineIn = srInFile.ReadLine
                    bytesRead += strLineIn.Length + intTerminatorSize

                    strLineIn = strLineIn.Trim

                    If intCurrentLine = 1 Then
                        ' Header line; skip it
                    ElseIf strLineIn.Length > 0 Then

                        ' Split the line
                        strSplitLine = strLineIn.Split(ControlChars.Tab)

                        If strSplitLine.Length >= 4 Then
                            If intProteinMapInfoCount >= udtProteinMapInfo.Length Then
                                ReDim Preserve udtProteinMapInfo(udtProteinMapInfo.Length * 2 - 1)
                            End If

                            If strCurrentProtein.Length = 0 OrElse strCurrentProtein <> strSplitLine(0) Then
                                ' Determine the Protein ID for this protein

                                strCurrentProtein = strSplitLine(0)

                                If Not dctProteinList.TryGetValue(strCurrentProtein, intCurrentProteinID) Then
                                    ' New protein; add it, assigning it index htProteinList.Count
                                    intCurrentProteinID = dctProteinList.Count
                                    dctProteinList.Add(strCurrentProtein, intCurrentProteinID)
                                End If

                            End If

                            With udtProteinMapInfo(intProteinMapInfoCount)
                                .ProteinID = intCurrentProteinID
                                .Peptide = strSplitLine(1)
                                .ResidueStart = Integer.Parse(strSplitLine(2))
                                .ResidueEnd = Integer.Parse(strSplitLine(3))
                            End With

                            intProteinMapInfoCount += 1
                        End If

                    End If
                    If intCurrentLine Mod 1000 = 0 Then
                        UpdateProgress(PERCENT_COMPLETE_POSTPROCESSING +
                           CSng((bytesRead / srInFile.BaseStream.Length) * 100) * (PERCENT_COMPLETE_POSTPROCESSING - PERCENT_COMPLETE_RUNNING_PROTEIN_COVERAGE_SUMMARIZER) / 100)
                    End If
                    intCurrentLine += 1

                Loop

            End Using

            ' Populate strProteins() and intProteinIDPointerArray() using htProteinList
            ReDim strProteins(dctProteinList.Count - 1)
            ReDim intProteinIDPointerArray(dctProteinList.Count - 1)

            ' Note: the Keys and Values are not necessarily sorted, but will be copied in the identical order
            dctProteinList.Keys.CopyTo(strProteins, 0)
            dctProteinList.Values.CopyTo(intProteinIDPointerArray, 0)

            ' Shrink udtProteinMapInfo to the appropriate length
            ReDim Preserve udtProteinMapInfo(intProteinMapInfoCount - 1)

            blnSuccess = True

        Catch ex As Exception
            mStatusMessage = "Error reading the newly created protein to peptide mapping file (" & Path.GetFileName(strProteinToPeptideMappingFilePath) & ")"
            HandleException(mStatusMessage, ex)
        End Try

        Return blnSuccess

    End Function

    Protected Function PreProcessInspectResultsFile(strInputFilePath As String,
       strOutputFolderPath As String,
       strInspectParameterFilePath As String) As String

        ' Read strInspectParameterFilePath to extract the mod names
        If Not ExtractModInfoFromInspectParamFile(strInspectParameterFilePath, mInspectModNameList) Then
            If mInspectModNameList.Count = 0 Then
                mInspectModNameList.Add("phos")
            End If
        End If

        Return PreProcessPSMResultsFile(strInputFilePath, strOutputFolderPath, ePeptideInputFileFormatConstants.InspectResultsFile)

    End Function

    Protected Function PreProcessPSMResultsFile(strInputFilePath As String,
                                                strOutputFolderPath As String,
                                                eFileType As ePeptideInputFileFormatConstants) As String


        Dim strPeptideListFilePath As String = String.Empty

        Dim intTerminatorSize As Integer

        Dim strLineIn As String

        Dim chSepChars = New Char() {ControlChars.Tab}
        Dim strSplitLine As String()

        Dim intCurrentLine As Integer
        Dim bytesRead As Long = 0

        Dim intModPeptidesFound As Integer

        Dim peptideSequenceColIndex As Integer
        Dim scanColIndex As Integer
        Dim strToolDescription As String = String.Empty

        If eFileType = ePeptideInputFileFormatConstants.InspectResultsFile Then
            ' Assume inspect results file line terminators are only a single byte (it doesn't matter if the terminators are actually two bytes)
            intTerminatorSize = 1

            ' The 3rd column in the Inspect results file should have the peptide sequence
            peptideSequenceColIndex = 2
            scanColIndex = 1
            strToolDescription = "Inspect"

        ElseIf eFileType = ePeptideInputFileFormatConstants.MSGFDBResultsFile Then
            intTerminatorSize = 2
            peptideSequenceColIndex = -1
            scanColIndex = -1
            strToolDescription = "MSGF-DB"

        Else
            mStatusMessage = "Unrecognized file type: " & eFileType.ToString() & "; will look for column header 'Peptide'"

            intTerminatorSize = 2
            peptideSequenceColIndex = -1
            scanColIndex = -1
            strToolDescription = "Generic PSM result file"
        End If

        Try
            If Not File.Exists(strInputFilePath) Then
                SetBaseClassErrorCode(eProcessFilesErrorCodes.InvalidInputFilePath)
                mStatusMessage = "File not found: " & strInputFilePath

                If Me.ShowMessages Then
                    ShowErrorMessage(mStatusMessage)
                Else
                    Throw New Exception(mStatusMessage)
                End If

                Exit Try

            End If

            ShowMessage("Pre-processing the " & strToolDescription & " results file: " & Path.GetFileName(strInputFilePath))

            ' Initialize the peptide list
            If mUniquePeptideList Is Nothing Then
                mUniquePeptideList = New SortedList(Of String, SortedSet(Of Integer))
            Else
                mUniquePeptideList.Clear()
            End If

            ' Open the PSM results file and construct a unique list of peptides in the file (including any modification symbols)
            ' Keep track of PSM counts
            Using srInFile = New StreamReader(New FileStream(strInputFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))

                intModPeptidesFound = 0
                intCurrentLine = 1
                Do While Not srInFile.EndOfStream
                    If mAbortProcessing Then Exit Do

                    strLineIn = srInFile.ReadLine
                    bytesRead += strLineIn.Length + intTerminatorSize

                    strLineIn = strLineIn.Trim

                    If intCurrentLine = 1 AndAlso (peptideSequenceColIndex < 0 OrElse strLineIn.StartsWith("#")) Then

                        ' Header line
                        If peptideSequenceColIndex < 0 Then
                            ' Split the header line to look for the "Peptide" and Scan columns
                            strSplitLine = strLineIn.Split(chSepChars)
                            For intIndex = 0 To strSplitLine.Length - 1
                                If peptideSequenceColIndex < 0 AndAlso strSplitLine(intIndex).ToLower() = "peptide" Then
                                    peptideSequenceColIndex = intIndex
                                End If

                                If scanColIndex < 0 AndAlso strSplitLine(intIndex).ToLower().StartsWith("scan") Then
                                    scanColIndex = intIndex
                                End If
                            Next

                            If peptideSequenceColIndex < 0 Then
                                SetBaseClassErrorCode(eProcessFilesErrorCodes.LocalizedError)
                                mStatusMessage = "Peptide column not found; unable to continue"

                                If Me.ShowMessages Then
                                    ShowErrorMessage(mStatusMessage)
                                Else
                                    Throw New Exception(mStatusMessage)
                                End If
                                Return String.Empty

                            End If

                        End If

                    ElseIf strLineIn.Length > 0 Then

                        strSplitLine = strLineIn.Split(chSepChars)

                        If strSplitLine.Length > peptideSequenceColIndex Then
                            Dim scanNumber As Integer
                            If scanColIndex >= 0 Then
                                If Not Integer.TryParse(strSplitLine(scanColIndex), scanNumber) Then
                                    scanNumber = 0
                                End If
                            End If

                            UpdateUniquePeptideList(strSplitLine(peptideSequenceColIndex), scanNumber)
                        End If

                    End If

                    If intCurrentLine Mod 1000 = 0 Then
                        UpdateProgress(PERCENT_COMPLETE_PREPROCESSING +
                           CSng((bytesRead / srInFile.BaseStream.Length) * 100) * (PERCENT_COMPLETE_RUNNING_PROTEIN_COVERAGE_SUMMARIZER - PERCENT_COMPLETE_PREPROCESSING) / 100)
                    End If

                    intCurrentLine += 1

                Loop

            End Using

            strPeptideListFilePath = PreProcessDataWriteOutPeptides(strInputFilePath, strOutputFolderPath)

        Catch ex As Exception
            mStatusMessage = "Error reading " & strToolDescription & " input file in PreProcessPSMResultsFile"
            HandleException(mStatusMessage, ex)

            strPeptideListFilePath = String.Empty
        End Try

        Return strPeptideListFilePath

    End Function

    Protected Function PreProcessPHRPDataFile(strInputFilePath As String, strOutputFolderPath As String) As String

        Dim strPeptideListFilePath As String = String.Empty

        Try
            If Not File.Exists(strInputFilePath) Then
                SetBaseClassErrorCode(eProcessFilesErrorCodes.InvalidInputFilePath)
                mStatusMessage = "File not found: " & strInputFilePath

                If Me.ShowMessages Then
                    ShowErrorMessage(mStatusMessage)
                Else
                    Throw New Exception(mStatusMessage)
                End If

                Exit Try

            End If

            Console.WriteLine()
            ShowMessage("Pre-processing PHRP data file: " & Path.GetFileName(strInputFilePath))

            ' Initialize the peptide list
            If mUniquePeptideList Is Nothing Then
                mUniquePeptideList = New SortedList(Of String, SortedSet(Of Integer))
            Else
                mUniquePeptideList.Clear()
            End If

            ' Initialize the PHRP startup options
            Dim oStartupOptions = New clsPHRPStartupOptions() With {
                .LoadModsAndSeqInfo = False,
                .LoadMSGFResults = False,
                .LoadScanStatsData = False,
                .MaxProteinsPerPSM = 1
            }

            ' Open the PHRP data file and construct a unique list of peptides in the file (including any modification symbols).
            ' MSPathFinder synopsis files do not have mod symbols in the peptides.
            ' This is OK since the peptides in mUniquePeptideList will have mod symbols removed in PreProcessDataWriteOutPeptides 
            ' when finding proteins that contain the peptides.
            Using objReader As New clsPHRPReader(strInputFilePath, clsPHRPReader.ePeptideHitResultType.Unknown, oStartupOptions)
                objReader.EchoMessagesToConsole = True
                objReader.SkipDuplicatePSMs = False

                For Each strErrorMessage As String In objReader.ErrorMessages
                    ShowErrorMessage(strErrorMessage)
                Next

                For Each strWarningMessage As String In objReader.WarningMessages
                    ShowMessage("Warning: " & strWarningMessage)
                Next

                objReader.ClearErrors()
                objReader.ClearWarnings()

                AddHandler objReader.ErrorEvent, AddressOf PHRPReader_ErrorEvent
                AddHandler objReader.WarningEvent, AddressOf PHRPReader_WarningEvent

                Do While objReader.MoveNext()
                    If mAbortProcessing Then Exit Do

                    UpdateUniquePeptideList(objReader.CurrentPSM.Peptide, objReader.CurrentPSM.ScanNumber)

                    If mUniquePeptideList.Count Mod 1000 = 0 Then
                        UpdateProgress(PERCENT_COMPLETE_PREPROCESSING +
                           objReader.PercentComplete * (PERCENT_COMPLETE_RUNNING_PROTEIN_COVERAGE_SUMMARIZER - PERCENT_COMPLETE_PREPROCESSING) / 100)
                    End If
                Loop
            End Using

            strPeptideListFilePath = PreProcessDataWriteOutPeptides(strInputFilePath, strOutputFolderPath)

        Catch ex As Exception
            mStatusMessage = "Error reading PSM input file in PreProcessPHRPDataFile"
            HandleException(mStatusMessage, ex)

            strPeptideListFilePath = String.Empty
        End Try

        Return strPeptideListFilePath

    End Function

    Protected Function PreProcessDataWriteOutPeptides(strInputFilePath As String, strOutputFolderPath As String) As String

        Dim strPeptideListFileName As String
        Dim strPeptideListFilePath As String = String.Empty

        Dim intModPeptidesFound As Integer

        Try

            ' Now write out the unique list of peptides to strPeptideListFilePath
            strPeptideListFileName = Path.GetFileNameWithoutExtension(strInputFilePath) & FILENAME_SUFFIX_PSM_UNIQUE_PEPTIDES & ".txt"

            If Not String.IsNullOrEmpty(strOutputFolderPath) Then
                strPeptideListFilePath = Path.Combine(strOutputFolderPath, strPeptideListFileName)
            Else
                Dim ioFileInfo As FileInfo
                ioFileInfo = New FileInfo(strInputFilePath)

                strPeptideListFilePath = Path.Combine(ioFileInfo.DirectoryName, strPeptideListFileName)
            End If

            LogMessage("Creating " & Path.GetFileName(strPeptideListFileName))

            ' Open the output file
            Using swOutFile = New StreamWriter(New FileStream(strPeptideListFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))

                ' Write out the peptides, removing any mod symbols that might be present
                For Each peptideEntry In mUniquePeptideList
                    Dim strPeptide As String

                    If mInspectModNameList.Count > 0 Then
                        strPeptide = RemoveInspectMods(peptideEntry.Key, mInspectModNameList)
                        If strPeptide <> peptideEntry.Key Then
                            intModPeptidesFound += 1
                        End If
                    Else
                        strPeptide = peptideEntry.Key
                    End If

                    If peptideEntry.Value.Count = 0 Then
                        swOutFile.WriteLine(strPeptide & ControlChars.Tab & "0")
                    Else
                        For Each scanNumber In peptideEntry.Value
                            swOutFile.WriteLine(strPeptide & ControlChars.Tab & scanNumber)
                        Next
                    End If

                Next
            End Using

        Catch ex As Exception
            mStatusMessage = "Error writing the Unique Peptides file in PreProcessDataWriteOutPeptides"
            HandleException(mStatusMessage, ex)

            strPeptideListFilePath = String.Empty
        End Try

        Return strPeptideListFilePath

    End Function

    Public Overloads Overrides Function ProcessFile(strInputFilePath As String, strOutputFolderPath As String, strParameterFilePath As String, blnResetErrorCode As Boolean) As Boolean

        Dim blnSuccess As Boolean

        Dim strInputFilePathWork As String
        Dim outputFileBaseName As String

        Dim strProteinToPeptideMappingFilePath As String = String.Empty

        Dim eInputFileFormat As ePeptideInputFileFormatConstants

        If blnResetErrorCode Then
            MyBase.SetBaseClassErrorCode(eProcessFilesErrorCodes.NoError)
        End If

        Try
            If strInputFilePath Is Nothing OrElse strInputFilePath.Length = 0 Then
                ShowMessage("Input file name is empty")
                MyBase.SetBaseClassErrorCode(eProcessFilesErrorCodes.InvalidInputFilePath)
            Else
                ' Note that CleanupFilePaths() will update mOutputFolderPath, which is used by LogMessage()
                If Not CleanupFilePaths(strInputFilePath, strOutputFolderPath) Then
                    MyBase.SetBaseClassErrorCode(eProcessFilesErrorCodes.FilePathError)
                Else

                    LogMessage("Processing " & Path.GetFileName(strInputFilePath))

                    If mPeptideInputFileFormat = ePeptideInputFileFormatConstants.AutoDetermine Then
                        eInputFileFormat = DetermineResultsFileFormat(strInputFilePath)
                    Else
                        eInputFileFormat = mPeptideInputFileFormat
                    End If

                    If eInputFileFormat = ePeptideInputFileFormatConstants.Unknown Then
                        ShowMessage("Input file type not recognized")
                        Return False
                    End If

                    UpdateProgress("Preprocessing input file", PERCENT_COMPLETE_PREPROCESSING)
                    mInspectModNameList.Clear()

                    Select Case eInputFileFormat
                        Case ePeptideInputFileFormatConstants.InspectResultsFile
                            ' Inspect search results file; need to pre-process it
                            strInputFilePathWork = PreProcessInspectResultsFile(strInputFilePath, strOutputFolderPath, mInspectParameterFilePath)
                            outputFileBaseName = Path.GetFileNameWithoutExtension(strInputFilePath)

                            mProteinCoverageSummarizer.PeptideFileFormatCode = clsProteinCoverageSummarizer.ePeptideFileColumnOrderingCode.SequenceOnly
                            mProteinCoverageSummarizer.PeptideFileSkipFirstLine = False
                            mProteinCoverageSummarizer.MatchPeptidePrefixAndSuffixToProtein = False

                        Case ePeptideInputFileFormatConstants.MSGFDBResultsFile
                            ' MSGF-DB search results file; need to pre-process it
                            ' Make sure RemoveSymbolCharacters is true
                            Me.RemoveSymbolCharacters = True

                            strInputFilePathWork = PreProcessPSMResultsFile(strInputFilePath, strOutputFolderPath, eInputFileFormat)
                            outputFileBaseName = Path.GetFileNameWithoutExtension(strInputFilePath)

                            mProteinCoverageSummarizer.PeptideFileFormatCode = clsProteinCoverageSummarizer.ePeptideFileColumnOrderingCode.SequenceOnly
                            mProteinCoverageSummarizer.PeptideFileSkipFirstLine = False
                            mProteinCoverageSummarizer.MatchPeptidePrefixAndSuffixToProtein = False

                        Case ePeptideInputFileFormatConstants.PHRPFile
                            ' Sequest, X!Tandem, Inspect, or MSGF-DB PHRP data file; need to pre-process it
                            ' Make sure RemoveSymbolCharacters is true
                            Me.RemoveSymbolCharacters = True

                            ' Open the PHRP data files and construct a unique list of peptides in the file (including any modification symbols)
                            ' Write the unique peptide list to _syn_peptides.txt
                            strInputFilePathWork = PreProcessPHRPDataFile(strInputFilePath, strOutputFolderPath)
                            outputFileBaseName = Path.GetFileNameWithoutExtension(strInputFilePath)

                            mProteinCoverageSummarizer.PeptideFileFormatCode = clsProteinCoverageSummarizer.ePeptideFileColumnOrderingCode.SequenceOnly
                            mProteinCoverageSummarizer.PeptideFileSkipFirstLine = False
                            mProteinCoverageSummarizer.MatchPeptidePrefixAndSuffixToProtein = False

                        Case Else
                            ' Pre-process the file to check for a header line
                            strInputFilePathWork = String.Copy(strInputFilePath)
                            outputFileBaseName = String.Empty

                            If eInputFileFormat = ePeptideInputFileFormatConstants.ProteinAndPeptideFile Then
                                mProteinCoverageSummarizer.PeptideFileFormatCode = clsProteinCoverageSummarizer.ePeptideFileColumnOrderingCode.ProteinName_PeptideSequence
                            Else
                                mProteinCoverageSummarizer.PeptideFileFormatCode = clsProteinCoverageSummarizer.ePeptideFileColumnOrderingCode.SequenceOnly
                            End If

                            If IsHeaderLinePresent(strInputFilePath, eInputFileFormat) Then
                                mProteinCoverageSummarizer.PeptideFileSkipFirstLine = True
                            End If

                    End Select

                    If String.IsNullOrWhiteSpace(strInputFilePathWork) Then
                        Return False
                    End If

                    UpdateProgress("Running protein coverage summarizer", PERCENT_COMPLETE_RUNNING_PROTEIN_COVERAGE_SUMMARIZER)

                    ' Call mProteinCoverageSummarizer.ProcessFile to perform the work
                    blnSuccess = mProteinCoverageSummarizer.ProcessFile(strInputFilePathWork, strOutputFolderPath,
                                                                        strParameterFilePath, True,
                                                                        strProteinToPeptideMappingFilePath, outputFileBaseName)
                    If Not blnSuccess Then
                        mStatusMessage = "Error running ProteinCoverageSummarizer: " & mProteinCoverageSummarizer.ErrorMessage
                    End If

                    If blnSuccess AndAlso strProteinToPeptideMappingFilePath.Length > 0 Then
                        UpdateProgress("Postprocessing", PERCENT_COMPLETE_POSTPROCESSING)

                        Select Case eInputFileFormat
                            Case ePeptideInputFileFormatConstants.PeptideListFile, ePeptideInputFileFormatConstants.ProteinAndPeptideFile
                                ' No post-processing is required

                            Case Else
                                ' Sequest, X!Tandem, Inspect, or MSGF-DB PHRP data file; need to post-process the results file
                                blnSuccess = PostProcessPSMResultsFile(strInputFilePathWork, strProteinToPeptideMappingFilePath, mDeleteTempFiles)

                        End Select
                    End If

                    If blnSuccess Then
                        LogMessage("Processing successful")
                        OperationComplete()
                    Else
                        LogMessage("Processing not successful")
                    End If
                End If
            End If

        Catch ex As Exception
            HandleException("Error in ProcessFile", ex)
            blnSuccess = False
        End Try

        Return blnSuccess

    End Function

    Protected Function RemoveInspectMods(strPeptide As String, ByRef lstInspectModNames As List(Of String)) As String

        Dim strPrefix As String = String.Empty
        Dim strSuffix As String = String.Empty

        If strPeptide.Length >= 4 Then
            If strPeptide.Chars(1) = "."c AndAlso
               strPeptide.Chars(strPeptide.Length - 2) = "."c Then
                strPrefix = strPeptide.Substring(0, 2)
                strSuffix = strPeptide.Substring(strPeptide.Length - 2, 2)

                strPeptide = strPeptide.Substring(2, strPeptide.Length - 4)
            End If
        End If

        For Each strModName As String In lstInspectModNames
            strPeptide = strPeptide.Replace(strModName, String.Empty)
        Next

        Return strPrefix & strPeptide & strSuffix

    End Function

    ''' <summary>
    ''' Add peptideSequence to mUniquePeptideList if not defined, including tracking the scanNumber
    ''' Otherwise, update the scan list for the peptide
    ''' </summary>
    ''' <param name="peptideSequence"></param>
    ''' <param name="scanNumber"></param>
    Private Sub UpdateUniquePeptideList(peptideSequence As String, scanNumber As Integer)

        Dim scanList As SortedSet(Of Integer) = Nothing
        If mUniquePeptideList.TryGetValue(peptideSequence, scanList) Then
            If Not scanList.Contains(scanNumber) Then
                scanList.Add(scanNumber)
            End If
        Else
            scanList = New SortedSet(Of Integer)
            scanList.Add(scanNumber)
            mUniquePeptideList.Add(peptideSequence, scanList)
        End If

    End Sub

#Region "Protein Coverage Summarizer Event Handlers"
    Private Sub mProteinCoverageSummarizer_ProgressChanged(taskDescription As String, percentComplete As Single) Handles mProteinCoverageSummarizer.ProgressChanged
        Dim sngPercentCompleteEffective As Single

        sngPercentCompleteEffective = PERCENT_COMPLETE_RUNNING_PROTEIN_COVERAGE_SUMMARIZER +
           percentComplete * CSng((PERCENT_COMPLETE_POSTPROCESSING - PERCENT_COMPLETE_RUNNING_PROTEIN_COVERAGE_SUMMARIZER) / 100.0)

        UpdateProgress(taskDescription, sngPercentCompleteEffective)
    End Sub

    Private Sub mProteinCoverageSummarizer_ProgressComplete() Handles mProteinCoverageSummarizer.ProgressComplete
        OperationComplete()
    End Sub

    Private Sub mProteinCoverageSummarizer_ProgressReset() Handles mProteinCoverageSummarizer.ProgressReset
        ResetProgress(mProteinCoverageSummarizer.ProgressStepDescription)
    End Sub
#End Region


#Region "PHRPReader Event Handlers"

    Private Sub PHRPReader_ErrorEvent(strErrorMessage As String)
        ShowErrorMessage(strErrorMessage)
    End Sub

    Private Sub PHRPReader_WarningEvent(strWarningMessage As String)
        ShowMessage("Warning: " & strWarningMessage)
    End Sub

#End Region

#Region "IComparer Classes"
    Protected Class ProteinIDMapInfoComparer
        Implements IComparer

        Public Function Compare(x As Object, y As Object) As Integer Implements IComparer.Compare
            Dim xData = CType(x, udtProteinIDMapInfoType)
            Dim yData = CType(y, udtProteinIDMapInfoType)

            If xData.Peptide > yData.Peptide Then
                Return 1
            ElseIf xData.Peptide < yData.Peptide Then
                Return -1
            Else
                If xData.ProteinID > yData.ProteinID Then
                    Return 1
                ElseIf xData.ProteinID < yData.ProteinID Then
                    Return -1
                Else
                    Return 0
                End If
            End If

        End Function
    End Class

    Protected Class ProteinIDMapInfoPeptideSearchComparer
        Implements IComparer

        Public Function Compare(x As Object, y As Object) As Integer Implements IComparer.Compare
            Dim xData = CType(x, udtProteinIDMapInfoType)
            Dim strPeptide = CType(y, String)

            If xData.Peptide > strPeptide Then
                Return 1
            ElseIf xData.Peptide < strPeptide Then
                Return -1
            Else
                Return 0
            End If

        End Function
    End Class

    Protected Class PepToProteinMappingComparer
        Implements IComparer(Of udtPepToProteinMappingType)

        Public Function Compare(x As udtPepToProteinMappingType, y As udtPepToProteinMappingType) As Integer Implements IComparer(Of udtPepToProteinMappingType).Compare

            If x.Peptide > y.Peptide Then
                Return 1
            ElseIf x.Peptide < y.Peptide Then
                Return -1
            Else
                If x.Protein > y.Protein Then
                    Return 1
                ElseIf x.Protein < y.Protein Then
                    Return -1
                Else
                    Return 0
                End If
            End If

        End Function

    End Class
#End Region

End Class
