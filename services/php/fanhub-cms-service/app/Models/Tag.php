<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsToMany;
use Illuminate\Support\Str;

class Tag extends Model
{
    use HasFactory;

    protected $table = 'cms_tags';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'name',
    ];

    protected static function booted(): void
    {
        static::creating(function (Tag $tag) {
            if (empty($tag->id)) {
                $tag->id = 'tag_' . Str::lower(Str::random(12));
            }
        });
    }

    /**
     * Quan hệ tới các bài viết mang thẻ này
     */
    public function posts(): BelongsToMany
    {
        return $this->belongsToMany(Post::class, 'cms_content_tags', 'tag_id', 'content_id');
    }
}
