# Object storage for the observability backends (Loki, Tempo, long-term metrics).
resource "aws_s3_bucket" "observability" {
  for_each = var.observability_buckets

  bucket        = "${local.name_prefix}-${each.key}-${data.aws_caller_identity.current.account_id}"
  force_destroy = false
}

resource "aws_s3_bucket_public_access_block" "observability" {
  for_each = aws_s3_bucket.observability

  bucket                  = each.value.id
  block_public_acls       = true
  block_public_policy     = true
  ignore_public_acls      = true
  restrict_public_buckets = true
}

resource "aws_s3_bucket_server_side_encryption_configuration" "observability" {
  for_each = aws_s3_bucket.observability

  bucket = each.value.id

  rule {
    bucket_key_enabled = true

    apply_server_side_encryption_by_default {
      sse_algorithm     = "aws:kms"
      kms_master_key_id = aws_kms_key.main.arn
    }
  }
}

resource "aws_s3_bucket_versioning" "observability" {
  for_each = aws_s3_bucket.observability

  bucket = each.value.id

  versioning_configuration {
    status = "Enabled"
  }
}

resource "aws_s3_bucket_lifecycle_configuration" "observability" {
  for_each = aws_s3_bucket.observability

  bucket = each.value.id

  rule {
    id     = "expire-telemetry"
    status = "Enabled"

    filter {}

    expiration {
      days = var.observability_retention_days
    }

    noncurrent_version_expiration {
      noncurrent_days = 7
    }

    abort_incomplete_multipart_upload {
      days_after_initiation = 7
    }
  }

  depends_on = [aws_s3_bucket_versioning.observability]
}

data "aws_iam_policy_document" "observability_tls_only" {
  for_each = aws_s3_bucket.observability

  statement {
    sid       = "DenyInsecureTransport"
    effect    = "Deny"
    actions   = ["s3:*"]
    resources = [each.value.arn, "${each.value.arn}/*"]

    principals {
      type        = "*"
      identifiers = ["*"]
    }

    condition {
      test     = "Bool"
      variable = "aws:SecureTransport"
      values   = ["false"]
    }
  }
}

resource "aws_s3_bucket_policy" "observability" {
  for_each = aws_s3_bucket.observability

  bucket = each.value.id
  policy = data.aws_iam_policy_document.observability_tls_only[each.key].json

  depends_on = [aws_s3_bucket_public_access_block.observability]
}
