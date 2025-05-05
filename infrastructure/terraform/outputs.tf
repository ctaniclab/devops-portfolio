output "cluster_endpoint" {
  description = "Endpoint for EKS control plane"
  value       = aws_eks_cluster.portfolio.endpoint
}

output "cluster_security_group_id" {
  description = "Security group ids attached to the cluster"
  value       = aws_eks_cluster.portfolio.vpc_config[0].security_group_ids
}

output "region" {
  description = "AWS region"
  value       = var.region
}