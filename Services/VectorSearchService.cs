using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using FastBertTokenizer;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace meow.Services
{
    public class VectorSearchService : IDisposable
    {
        private readonly QdrantClient _qdrantClient;
        private readonly InferenceSession _onnxSession;
        private readonly BertTokenizer _tokenizer;

        public VectorSearchService(IConfiguration configuration, IWebHostEnvironment env)
        {
            var modelPath = Path.Combine(env.WebRootPath, "ai", "model.onnx");
            var vocabPath = Path.Combine(env.WebRootPath, "ai", "vocab.txt");

            _onnxSession = new InferenceSession(modelPath);
            _tokenizer = new BertTokenizer();
            
            using (var reader = new StreamReader(vocabPath))
            {
                _tokenizer.LoadVocabulary(reader, true);
            }
            
            _qdrantClient = new QdrantClient(configuration["Qdrant:Host"] ?? "localhost", 6334);
        }

        public async Task UpsertBook(int id, string title, string description)
{
    var vector = GenerateVector(description);
    await _qdrantClient.UpsertAsync("books_collection", new[]
    {
        new PointStruct
        {
            Id = (ulong)id,
            Vectors = vector,
            Payload = { ["title"] = title }
        }
    });
}

        public float[] GenerateVector(string text)
        {
            var tokens = _tokenizer.Encode(text, 256);
            long[] inputIds = tokens.InputIds.ToArray();
            var inputTensor = new DenseTensor<long>(inputIds, new[] { 1, inputIds.Length });
            
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor("input_ids", inputTensor) };
            using var results = _onnxSession.Run(inputs);
            
            return results.First().AsEnumerable<float>().ToArray();
        }

        public void Dispose()
        {
            _onnxSession?.Dispose();
        }
    }
}