<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Illuminate\Support\Str;

class Comment extends Model
{
    use HasFactory;

    protected $table = 'comments';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'target_id',
        'user_id',
        'parent_id',
        'title',
        'body',
        'status',
    ];

    protected $appends = [
        'description',
    ];

    protected static function booted(): void
    {
        static::creating(function (Comment $comment) {
            if (empty($comment->id)) {
                $comment->id = 'req_' . Str::lower(Str::random(12));
            }
            if (empty($comment->status)) {
                $comment->status = 'active';
            }
        });
    }

    public function getDescriptionAttribute(): ?string
    {
        return $this->attributes['body'] ?? null;
    }

    public function setDescriptionAttribute(?string $value): void
    {
        $this->attributes['body'] = $value;
    }

    public function post(): BelongsTo
    {
        return $this->belongsTo(Post::class, 'target_id');
    }

    public function parent(): BelongsTo
    {
        return $this->belongsTo(Comment::class, 'parent_id');
    }

    public function children(): HasMany
    {
        return $this->hasMany(Comment::class, 'parent_id');
    }
}
