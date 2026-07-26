using Mafi;
using Mafi.Core.Products;
using Mafi.Core.Prototypes;
using System.Collections.Generic;

namespace Iserik.FaFOptimiser.Services
{
    public class ChainNode
    {
        public bool IsFarm { get; set; }
        public bool IsBaseResource { get; set; }
        public bool IsChickFarm { get; set; }
        public string MachineName { get; set; }
        public double MachineCount { get; set; }
        public ProductProto OutputProduct { get; set; }
        public Fix32 OutputAmount { get; set; }
        public IProtoWithIcon MachineProto { get; set; }
        public List<ChainNode> Inputs { get; set; } = new List<ChainNode>();

        public ChainNode Clone()
        {
            var clone = new ChainNode
            {
                IsFarm = this.IsFarm,
                IsBaseResource = this.IsBaseResource,
                IsChickFarm = this.IsChickFarm,
                MachineName = this.MachineName,
                MachineCount = this.MachineCount,
                OutputProduct = this.OutputProduct,
                MachineProto = this.MachineProto,
                OutputAmount = this.OutputAmount,
                Inputs = new List<ChainNode>()
            };
            foreach (var input in this.Inputs) clone.Inputs.Add(input.Clone());
            return clone;
        }
    }

    public class ResolvedChain
    {
        public ProductProto TargetProduct { get; set; }
        public Fix32 TargetAmount { get; set; }
        public ChainNode RootNode { get; set; }
        public List<ChainNode> PendingInputs { get; set; } = new List<ChainNode>();
        public double FarmsNeeded { get; set; }
        public Dictionary<ProductProto, Fix32> RawCropDemands { get; } = new Dictionary<ProductProto, Fix32>();
        public Dictionary<ProductProto, Fix32> NetByproducts { get; } = new Dictionary<ProductProto, Fix32>();
        public double ResourceScore { get; set; }
        public HashSet<ProductProto> SatisfiedProducts { get; } = new HashSet<ProductProto>();

        public void AddSatisfiedProduct(ProductProto product) => SatisfiedProducts.Add(product);

        public bool IsEquivalentTo(ResolvedChain other)
        {
            if (other == null) return false;
            if (!AreDictionariesEqual(this.NetByproducts, other.NetByproducts)) return false;
            return AreNodesEquivalent(this.RootNode, other.RootNode);
        }

        public Dictionary<ProductProto, Fix32> GetRequiredCrops()
        {
            var crops = new Dictionary<ProductProto, Fix32>();
            ExtractCropsFromNode(this.RootNode, crops);
            return crops;
        }

        public Dictionary<ProductProto, Fix32> GetRequiredChickFarmProducts()
        {
            var chickProducts = new Dictionary<ProductProto, Fix32>();
            ExtractChickFarmProductsFromNode(this.RootNode, chickProducts);
            return chickProducts;
        }

        public Dictionary<ProductProto, Fix32> GetRequiredInputs()
        {
            var inputs = new Dictionary<ProductProto, Fix32>();
            ExtractInputsFromNode(this.RootNode, inputs);
            return inputs;
        }

        private void ExtractInputsFromNode(ChainNode node, Dictionary<ProductProto, Fix32> inputs)
        {
            if (node == null) return;
            if (node.IsFarm || node.IsChickFarm || node.IsBaseResource || node.Inputs.Count == 0)
            {
                if (inputs.ContainsKey(node.OutputProduct)) inputs[node.OutputProduct] += node.OutputAmount;
                else inputs[node.OutputProduct] = node.OutputAmount;
                if (node.IsFarm || node.IsChickFarm) return;
            }
            foreach (var input in node.Inputs) ExtractInputsFromNode(input, inputs);
        }

        private void ExtractCropsFromNode(ChainNode node, Dictionary<ProductProto, Fix32> crops)
        {
            if (node == null) return;
            if (node.IsFarm)
            {
                if (crops.ContainsKey(node.OutputProduct)) crops[node.OutputProduct] += node.OutputAmount;
                else crops[node.OutputProduct] = node.OutputAmount;
            }
            // CRITICAL FIX: Stop traversing down if we hit a livestock product!
            if (node.IsChickFarm) return;

            foreach (var input in node.Inputs) ExtractCropsFromNode(input, crops);
        }

        private void ExtractChickFarmProductsFromNode(ChainNode node, Dictionary<ProductProto, Fix32> chickProducts)
        {
            if (node == null) return;
            if (node.IsChickFarm)
            {
                if (chickProducts.ContainsKey(node.OutputProduct)) chickProducts[node.OutputProduct] += node.OutputAmount;
                else chickProducts[node.OutputProduct] = node.OutputAmount;
                return; // Stop traversing once we hit the livestock product!
            }
            foreach (var input in node.Inputs) ExtractChickFarmProductsFromNode(input, chickProducts);
        }

        private bool AreNodesEquivalent(ChainNode nodeA, ChainNode nodeB)
        {
            if (nodeA == null && nodeB == null) return true;
            if (nodeA == null || nodeB == null) return false;
            if (nodeA.OutputProduct != nodeB.OutputProduct) return false;
            if (nodeA.IsFarm && nodeB.IsFarm) return true;
            if (nodeA.IsChickFarm && nodeB.IsChickFarm) return true;
            if (nodeA.MachineName != nodeB.MachineName) return false;

            var inputsA = new List<ChainNode>();
            foreach (var i in nodeA.Inputs) if (!i.IsBaseResource) inputsA.Add(i);
            inputsA.Sort((x, y) => (x.OutputProduct?.Id.Value ?? "").CompareTo(y.OutputProduct?.Id.Value ?? ""));

            var inputsB = new List<ChainNode>();
            foreach (var i in nodeB.Inputs) if (!i.IsBaseResource) inputsB.Add(i);
            inputsB.Sort((x, y) => (x.OutputProduct?.Id.Value ?? "").CompareTo(y.OutputProduct?.Id.Value ?? ""));

            if (inputsA.Count != inputsB.Count) return false;
            for (int i = 0; i < inputsA.Count; i++)
            {
                if (!AreNodesEquivalent(inputsA[i], inputsB[i])) return false;
            }
            return true;
        }

        private static bool AreDictionariesEqual(Dictionary<ProductProto, Fix32> dict1, Dictionary<ProductProto, Fix32> dict2)
        {
            if (dict1.Count != dict2.Count) return false;
            foreach (var kvp in dict1)
            {
                if (!dict2.TryGetValue(kvp.Key, out Fix32 val2)) return false;
                if (System.Math.Abs(kvp.Value.ToFloat() - val2.ToFloat()) > 0.05f) return false;
            }
            return true;
        }
    }
}