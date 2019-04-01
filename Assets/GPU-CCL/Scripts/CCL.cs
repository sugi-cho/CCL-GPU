using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CCL : MonoBehaviour
{
    public ComputeShader cclCompute;

    public int width = 512;
    public int height = 512;
    public int numMaxLabels = 32;
    public int numPerLabel = 128;

    public Material souceToInput;
    public Material visualizer;
    public Material blobMat;

    public Camera blobDrawer;
    MaterialPropertyBlock mpb;

    [SerializeField] RenderTexture inputTex;
    [SerializeField] RenderTexture labelTex;

    ComputeBuffer labelFlgBuffer;
    ComputeBuffer labelAppendBuffer;
    ComputeBuffer labelArgBuffer;
    ComputeBuffer labelDataAppendBuffer;
    ComputeBuffer labelDataBuffer;
    ComputeBuffer accumeLabelDataBuffer;

    [SerializeField] uint[] args;
    [SerializeField] LabelData[] labelData;

    Mesh quad
    {
        get
        {
            if (_q == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _q = go.GetComponent<MeshFilter>().sharedMesh;
                Destroy(go);
            }
            return _q;
        }
    }
    Mesh _q;

    [ContextMenu("test")]
    void test()
    {
        Debug.Log(1 >> 1);
    }

    [System.Serializable]
    public struct LabelData
    {
        public float size;
        public Vector2 pos;
    }

    private void Start()
    {
        inputTex = new RenderTexture(width, height, 16, RenderTextureFormat.R8);
        inputTex.Create();
        labelTex = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
        labelTex.filterMode = FilterMode.Point;
        labelTex.enableRandomWrite = true;
        labelTex.Create();

        labelFlgBuffer = new ComputeBuffer(width * height, sizeof(int));
        labelAppendBuffer = new ComputeBuffer(numMaxLabels, sizeof(int), ComputeBufferType.Append);
        labelArgBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        labelDataAppendBuffer = new ComputeBuffer(numPerLabel, sizeof(int) * 3, ComputeBufferType.Append);
        labelDataBuffer = new ComputeBuffer(numPerLabel * numMaxLabels, sizeof(float) * 3);
        accumeLabelDataBuffer = new ComputeBuffer(numMaxLabels, sizeof(float) * 3);
        args = new uint[] { quad.GetIndexCount(0), 0, 0, 0, 0 };
        labelData = new LabelData[numMaxLabels];
        mpb = new MaterialPropertyBlock();

        InvokeRepeating("DetectBlobs", 1f / 30f, 1f / 30f);
    }

    private void OnDestroy()
    {
        new List<RenderTexture>(new[] { inputTex, labelTex })
            .ForEach(rt => rt.Release());
        new List<ComputeBuffer>(new[] { labelFlgBuffer, labelAppendBuffer, labelArgBuffer, labelDataAppendBuffer, labelDataBuffer, accumeLabelDataBuffer })
            .ForEach(bf => bf.Dispose());
    }

    public void DetectBlobs()
    {
        var kernel = cclCompute.FindKernel("init");
        cclCompute.SetTexture(kernel, "inTex", inputTex);
        cclCompute.SetTexture(kernel, "labelTex", labelTex);
        cclCompute.SetInt("numMaxLabel", numMaxLabels);
        cclCompute.SetInt("texWidth", width);
        cclCompute.SetInt("texHeight", height);
        cclCompute.Dispatch(kernel, width / 8, height / 8, 1);

        kernel = cclCompute.FindKernel("columnWiseLabel");
        cclCompute.SetTexture(kernel, "labelTex", labelTex);
        cclCompute.Dispatch(kernel, width / 8, 1, 1);

        var itr = Mathf.Log(width, 2);
        var div = 2;
        for (var i = 0; i < itr; i++)
        {
            kernel = cclCompute.FindKernel("mergeLabels");
            cclCompute.SetTexture(kernel, "labelTex", labelTex);
            cclCompute.SetInt("div", div);

            cclCompute.Dispatch(kernel, Mathf.Max(width / (2 << i) / 8, 1), 1, 1);
            div *= 2;
        }

        kernel = cclCompute.FindKernel("clearLabelFlag");
        cclCompute.SetTexture(kernel, "labelTex", labelTex);
        cclCompute.SetBuffer(kernel, "labelBuffer", labelAppendBuffer);
        cclCompute.SetBuffer(kernel, "labelFlg", labelFlgBuffer);
        cclCompute.Dispatch(kernel, width * height / 8, 1, 1);

        kernel = cclCompute.FindKernel("setRootLabel");
        cclCompute.SetTexture(kernel, "labelTex", labelTex);
        cclCompute.SetBuffer(kernel, "labelFlg", labelFlgBuffer);
        cclCompute.Dispatch(kernel, width / 8, height / 8, 1);

        labelAppendBuffer.SetCounterValue(0);
        kernel = cclCompute.FindKernel("countLabel");
        cclCompute.SetBuffer(kernel, "labelFlg", labelFlgBuffer);
        cclCompute.SetBuffer(kernel, "labelAppend", labelAppendBuffer);
        cclCompute.Dispatch(kernel, width * height / 8, 1, 1);

        kernel = cclCompute.FindKernel("clearLabelData");
        cclCompute.SetBuffer(kernel, "labelDataBuffer", labelDataBuffer);
        cclCompute.Dispatch(kernel, numPerLabel * numMaxLabels / 8, 1, 1);

        for (var i = 0; i < numMaxLabels; i++)
        {
            cclCompute.SetInt("labelIdx", i);
            cclCompute.SetInt("numPerLabel", numPerLabel);

            labelDataAppendBuffer.SetCounterValue(0);
            kernel = cclCompute.FindKernel("clearLabelData");
            cclCompute.SetBuffer(kernel, "labelDataBuffer", labelDataAppendBuffer);
            cclCompute.Dispatch(kernel, numPerLabel / 8, 1, 1);

            kernel = cclCompute.FindKernel("appendLabelData");
            cclCompute.SetBuffer(kernel, "labelBuffer", labelAppendBuffer);
            cclCompute.SetTexture(kernel, "labelTex", labelTex);
            cclCompute.SetBuffer(kernel, "labelDataAppend", labelDataAppendBuffer);
            cclCompute.Dispatch(kernel, width / 8, height / 8, 1);

            kernel = cclCompute.FindKernel("setLabelData");
            cclCompute.SetBuffer(kernel, "inLabelDataBuffer", labelDataAppendBuffer);
            cclCompute.SetBuffer(kernel, "labelDataBuffer", labelDataBuffer);
            cclCompute.Dispatch(kernel, numPerLabel / 8, 1, 1);
        }

        kernel = cclCompute.FindKernel("clearLabelData");
        cclCompute.SetBuffer(kernel, "labelDataBuffer", accumeLabelDataBuffer);
        cclCompute.Dispatch(kernel, numMaxLabels / 8, 1, 1);

        kernel = cclCompute.FindKernel("buildBlobData");
        cclCompute.SetBuffer(kernel, "inLabelDataBuffer", labelDataBuffer);
        cclCompute.SetBuffer(kernel, "labelDataBuffer", accumeLabelDataBuffer);
        cclCompute.Dispatch(kernel, 1, numMaxLabels, 1);

        ComputeBuffer.CopyCount(labelAppendBuffer, labelArgBuffer, sizeof(uint));
        labelArgBuffer.GetData(args);
        accumeLabelDataBuffer.GetData(labelData);
    }

    Vector4 prop;
    private void Update()
    {
        prop.x = 1f / width;
        prop.y = 1f / height;
        prop.z = blobDrawer.orthographicSize;
        prop.w = blobDrawer.aspect * prop.z;
        mpb.SetVector("_Prop", prop);
        mpb.SetBuffer("_LabelBuffer", accumeLabelDataBuffer);
        Graphics.DrawMeshInstancedIndirect(
            quad, 0, blobMat, quad.bounds, labelArgBuffer, 0, mpb,
            UnityEngine.Rendering.ShadowCastingMode.Off, false, 0, blobDrawer);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        Graphics.Blit(source, inputTex, souceToInput);
        visualizer.SetTexture("_LabelTex", labelTex);
        Graphics.Blit(source, destination, visualizer);
    }
}
