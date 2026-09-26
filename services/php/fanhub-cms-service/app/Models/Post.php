<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;
use Illuminate\Support\Str;

class Post extends Model
{
    use HasFactory;

    protected $table = 'contents';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'user_id',
        'category_id',
        'group_id',
        'title',
        'body',
        'status',
        'likes_count',
        'comments_count',
    ];

    protected $appends = [
        'description',
    ];

    protected static function booted(): void
    {
        static::creating(function (Post $post) {
            if (empty($post->id)) {
                // Tự sinh ID dạng req_xxx như trong tài liệu đặc tả API
                $post->id = 'req_' . Str::lower(Str::random(12));
            }

            if (empty($post->status)) {
                $post->status = 'active';
            }
        });
    }

    /**
     * Map description tương ứng với body
     */
    public function getDescriptionAttribute(): ?string
    {
        return $this->attributes['body'] ?? null;
    }

    public function setDescriptionAttribute(?string $value): void
    {
        $this->attributes['body'] = $value;
    }

    /**
     * Quan hệ tới Category
     */
    public function category(): BelongsTo
    {
        return $this->belongsTo(Category::class, 'category_id');
    }

    /**
     * Quan hệ tới Reaction / Bookmark
     */
    public function reactions(): HasMany
    {
        return $this->hasMany(ReactionBookmark::class, 'target_id')
            ->where('target_type', 'content');
    }
}
