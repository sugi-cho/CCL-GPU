using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CCL : MonoBehaviour
{
    public ComputeShader cs;
    public Material binarization;
    [Header("calculation params")]
    [Range(0f, 1f)]
    public float threshold = 0.5f;
    public int width = 512;
    public int height = 512;
    public int maxBlobs = 512;

    ComputeBuffer labelFlagBuffer;
    ComputeBuffer labelAppendBuffer;
    ComputeBuffer pointAppendBuffer;
    ComputeBuffer countBuffer;
    int[] countData;
    Point[] pointData;
    int[] labels;
    bool labelCounted;

    [Header("output data")]
    public int numLabels;
    public int numBlobs;
    public Rect[] blobs { get; private set; }
    RenderTexture binaryTex;
    RenderTexture[] labelTexes;
    RenderTexture edgedTex;
    public RenderTexture output;

    public void Compute(Texture source)
    {
        if (binaryTex == null)
            Init();
        Binarization(source);
        BuildLabel();
        MargeLabel();
        MargeBoundary();
        MargeBoundary();
        //1回でマージできない時があるから念のため
        Graphics.CopyTexture(labelTexes[1], output);
        labelCounted = false;
    }
    public int CountLabels()
    {
        return numLabels = CountLabels(output);
    }
    public void BuildBlobs()
    {
        if (!labelCounted)
            numLabels = CountLabels();
        BuildBlobs(numLabels);
    }

    void Init()
    {
        width = Mathf.ClosestPowerOfTwo(Mathf.Max(width, 8));
        height = Mathf.ClosestPowerOfTwo(Mathf.Max(height, 8));

        binaryTex = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        binaryTex.enableRandomWrite = true;
        binaryTex.wrapMode = TextureWrapMode.Clamp;
        binaryTex.Create();
        labelTexes = new RenderTexture[2];
        for (var i = 0; i < 2; i++)
        {
            var labelTex = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
            labelTex.enableRandomWrite = true;
            labelTex.Create();

            labelTexes[i] = labelTex;
        }
        edgedTex = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        edgedTex.enableRandomWrite = true;
        edgedTex.Create();
        output = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        output.enableRandomWrite = true;
        output.filterMode = FilterMode.Point;
        output.Create();

        labelFlagBuffer = new ComputeBuffer(width * height, sizeof(int));
        labelAppendBuffer = new ComputeBuffer(width * height, sizeof(int), ComputeBufferType.Append);
        labelAppendBuffer.SetCounterValue(0);
        pointAppendBuffer = new ComputeBuffer(width * height, sizeof(float) * 2, ComputeBufferType.Append);
        pointAppendBuffer.SetCounterValue(0);
        countBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
        countData = new[] { 0 };
        pointData = new Point[width * height];

        blobs = new Rect[maxBlobs];
        labels = new int[maxBlobs];
    }

    private void OnDestroy()
    {
        new List<ComputeBuffer>(new[] { labelFlagBuffer, labelAppendBuffer, pointAppendBuffer, countBuffer })
            .ForEach(b => b.Dispose());
        new List<RenderTexture>(new[] { binaryTex, labelTexes[0], labelTexes[1] })
            .ForEach(rt => rt.Release());
    }

    void Binarization(Texture source)
    {
        source.wrapMode = TextureWrapMode.Clamp;
        binarization.SetFloat("_Threshold", threshold);
        Graphics.Blit(source, binaryTex, binarization);
    }

    void BuildLabel()
    {
        var kernel = cs.FindKernel("buildLabel");
        cs.SetTexture(kernel, "InTex", binaryTex);
        cs.SetTexture(kernel, "OutTex", labelTexes[1]);
        cs.SetInt("texWidth", width);
        cs.Dispatch(kernel, width / 8, height / 8, 1);
    }

    void MargeLabel()
    {
        SwapArray(labelTexes);
        var kernel = cs.FindKernel("updateLabel");
        cs.SetTexture(kernel, "InTex", labelTexes[0]);
        cs.SetTexture(kernel, "OutTex", labelTexes[1]);
        cs.Dispatch(kernel, width / 8, height / 8, 1);

        SwapArray(labelTexes);
        kernel = cs.FindKernel("margeLabel");
        cs.SetTexture(kernel, "InTex", labelTexes[0]);
        cs.SetTexture(kernel, "OutTex", labelTexes[1]);
        cs.SetInt("texWidth", width);
        cs.SetInt("texHeight", height);
        cs.Dispatch(kernel, width / 8, height / 8, 1);
    }

    void MargeBoundary()
    {
        SwapArray(labelTexes);
        var kernel = cs.FindKernel("margeBoundary");
        cs.SetTexture(kernel, "InTex", labelTexes[0]);
        cs.SetTexture(kernel, "OutTex", labelTexes[1]);
        cs.SetInt("texWidth", width);
        cs.SetInt("texHeight", height);
        cs.Dispatch(kernel, width / 8, height / 8, 1);

        SwapArray(labelTexes);
        kernel = cs.FindKernel("margeLabel");
        cs.SetTexture(kernel, "InTex", labelTexes[0]);
        cs.SetTexture(kernel, "OutTex", labelTexes[1]);
        cs.SetInt("texWidth", width);
        cs.SetInt("texHeight", height);
        cs.Dispatch(kernel, width / 8, height / 8, 1);
    }

    void Edging(RenderTexture rt0, RenderTexture rt1)
    {
        SwapArray(labelTexes);
        var kernel = cs.FindKernel("edging");
        cs.SetTexture(kernel, "InTex", rt0);
        cs.SetTexture(kernel, "OutTex", rt1);
        cs.Dispatch(kernel, width / 8, height / 8, 1);
    }

    int CountLabels(RenderTexture labelTex)
    {
        Edging(labelTex, edgedTex);

        labelAppendBuffer.SetCounterValue(0);
        var kernel = cs.FindKernel("clearLabelFlag");
        cs.SetBuffer(kernel, "LabelFlag", labelFlagBuffer);
        cs.Dispatch(kernel, width * height / 8, 1, 1);

        kernel = cs.FindKernel("setLabelFlag");
        cs.SetTexture(kernel, "InTex", edgedTex);
        cs.SetBuffer(kernel, "LabelFlag", labelFlagBuffer);
        cs.Dispatch(kernel, width / 8, height / 8, 1);

        kernel = cs.FindKernel("countLabel");
        cs.SetBuffer(kernel, "LabelFlag", labelFlagBuffer);
        cs.SetBuffer(kernel, "LabelAppend", labelAppendBuffer);
        cs.Dispatch(kernel, width * height / 8, 1, 1);

        countBuffer.SetData(countData);
        ComputeBuffer.CopyCount(labelAppendBuffer, countBuffer, 0);
        countBuffer.GetData(countData);

        labelCounted = true;
        return countData[0];
    }

    void BuildBlobs(int numLabels)
    {
        numBlobs = Mathf.Min(maxBlobs, numLabels);
        labelAppendBuffer.GetData(labels, 0, 0, numBlobs);

        var minX = 1f;
        var minY = 1f;
        var maxX = 0f;
        var maxY = 0f;
        for (var i = 0; i < numBlobs; i++)
        {
            pointAppendBuffer.SetCounterValue(0);

            var kernel = cs.FindKernel("getPointFromLabel");
            cs.SetTexture(kernel, "InTex", edgedTex);
            cs.SetBuffer(kernel, "PointAppend", pointAppendBuffer);
            cs.SetInt("targetLabel", labels[i]);
            cs.Dispatch(kernel, width / 8, height / 8, 1);

            countBuffer.SetData(countData);
            ComputeBuffer.CopyCount(pointAppendBuffer, countBuffer, 0);
            countBuffer.GetData(countData);

            var numPoints = countData[0];
            pointAppendBuffer.GetData(pointData, 0, 0, numPoints);
            minX = minY = 1f;
            maxX = maxY = 0f;

            for (var j = 0; j < numPoints; j++)
            {
                var point = pointData[j];
                minX = Mathf.Min(minX, (float)point.x / width);
                minY = Mathf.Min(minY, (float)point.y / height);
                maxX = Mathf.Max(maxX, (float)(point.x + 1) / width);
                maxY = Mathf.Max(maxY, (float)(point.y + 1) / height);
            }
            blobs[i] = Rect.MinMaxRect(minX, minY, maxX, maxY);
        }
    }

    void SwapArray<T>(T[] array)
    {
        var tmp = array[0];
        array[0] = array[1];
        array[1] = tmp;
    }

    struct Point
    {
        public int x;
        public int y;
    }
}
