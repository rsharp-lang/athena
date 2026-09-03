Imports System.Text.Encodings.Web
Imports System.Text.Json
Imports System.Text.Json.Serialization

''' <summary>
''' demo 项目之中统一使用的 json 序列化配置：
''' 大语言模型返回的数据字段命名与大小写往往并不严格，这里统一开启宽松读取。
''' </summary>
Public Module DemoJson

    ''' <summary>
    ''' 用于解析大语言模型输出与向网页回传数据的 json 序列化选项
    ''' </summary>
    ''' <returns></returns>
    Public ReadOnly Property Options As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True,
        .NumberHandling = JsonNumberHandling.AllowReadingFromString,
        .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        .Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        .WriteIndented = False
    }

    Public Function Serialize(obj As Object) As String
        Return JsonSerializer.Serialize(obj, Options)
    End Function

    Public Function Deserialize(Of T)(json As String) As T
        Return JsonSerializer.Deserialize(Of T)(json, Options)
    End Function

End Module
